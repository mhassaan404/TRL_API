// TRL end-to-end suite: builds the API, creates throwaway *_tmp databases (a catalog and two clients), starts the API
// on its own port, runs the test areas over HTTP and checks results in the database, then removes everything.
//
//   node e2e/run.mjs                    all areas
//   node e2e/run.mjs payments reports   only these areas
//   node e2e/run.mjs --keep             keep the databases afterwards (for debugging)
//   node e2e/run.mjs --teardown         only remove leftover E2E databases
//
// Never touches TRL_DB, TRL_Catalog or production: every database name is checked (TRL_E2E_ prefix + _tmp suffix)
// and every connection the API / TRL_Tools get is overridden and checked before they start.
import { execFileSync, spawn } from 'node:child_process'
import { existsSync, mkdirSync } from 'node:fs'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import {
  ADMIN_USER, BASE, CLIENT, DB, E2E_DIR, OUT, PORT, REPO, SafetyError, Session, SQL_SERVER, childEnv, createDatabase,
  databaseExists, dropDatabase, newPassword, results, selfCheck, sqlFile,
} from './lib.mjs'

// Order matters only for auth: it changes the password and uses up the login rate limit, so it runs last
const AREAS = ['leases', 'invoices', 'payments', 'deposits-settlement', 'reports', 'permissions', 'auth']

const args = process.argv.slice(2)
const keep = args.includes('--keep')
const teardownOnly = args.includes('--teardown')
const chosen = args.filter((a) => !a.startsWith('--'))
const unknown = chosen.filter((a) => !AREAS.includes(a))
if (unknown.length) {
  console.error(`Unknown area(s): ${unknown.join(', ')}. Areas: ${AREAS.join(', ')}`)
  process.exit(2)
}
const areas = chosen.length ? AREAS.filter((a) => chosen.includes(a)) : AREAS

const log = (m) => console.log(`[e2e] ${m}`)
const run = (cmd, cmdArgs, opts = {}) => execFileSync(cmd, cmdArgs, { cwd: REPO, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], ...opts })

const teardown = () => {
  for (const db of [DB.a, DB.b, DB.catalog]) {
    if (databaseExists(db)) {
      dropDatabase(db)
      log(`dropped ${db}`)
    }
  }
}

let api
const stopApi = () => {
  if (api && api.exitCode === null) api.kill()
}

const main = async () => {
  selfCheck()
  log(`SQL Server: ${SQL_SERVER}   API: ${BASE}`)
  try {
    run('sqlcmd', ['-?'])
  } catch (e) {
    if (e.code === 'ENOENT') throw new Error('sqlcmd was not found on PATH.')
  }
  if (teardownOnly) return teardown()

  // 1. Build into e2e/.out (the normal bin folder may be locked by a running API)
  mkdirSync(OUT, { recursive: true })
  log('building API and TRL_Tools ...')
  for (const [proj, dir] of [['TRL_API/TRL_API.csproj', 'api'], ['TRL_Tools/TRL_Tools.csproj', 'tools']]) {
    try {
      run('dotnet', ['build', proj, '-c', 'Release', '-o', path.join(OUT, dir), '-nologo', '-v', 'q',
        // A fresh file hash each build: Windows Smart App Control sometimes blocks a newly built DLL, and a
        // deterministic build would reproduce the same blocked file every time
        '-p:Deterministic=false'])
    } catch (e) {
      throw new Error(`Build of ${proj} failed:\n${e.stdout || e.message}`)
    }
  }

  // 2. Fresh databases: leftovers from an earlier run are removed first
  teardown()
  // TRL_E2E_PASSWORD: a known admin password, e.g. to log in to a --keep database from the browser
  const password = process.env.TRL_E2E_PASSWORD || newPassword()
  const env = childEnv({ TRL_ADMIN_PASSWORD: password })
  createDatabase(DB.catalog)
  sqlFile(DB.catalog, path.join(REPO, 'Database', 'catalog', '01_catalog.sql'))
  for (const [key, db] of [['a', DB.a], ['b', DB.b]]) {
    run('dotnet', [path.join(OUT, 'tools', 'TRL_Tools.dll'), 'provision', '--code', CLIENT[key], '--name', `E2E Client ${key.toUpperCase()}`,
      '--connection-key', CLIENT[key], '--admin-user', ADMIN_USER, '--create-database'], { env })
    if (!databaseExists(db)) throw new Error(`Provisioning ${CLIENT[key]} did not create ${db}.`)
  }
  log('databases ready')

  // 3. API on its own port with only the E2E connections
  const apiEnv = childEnv({
    ASPNETCORE_ENVIRONMENT: 'Production',
    JwtSettings__SecretKey: `e2e-${newPassword()}-${newPassword()}-0123456789abcdef`,
    JwtSettings__AccessTokenExpirationMinutes: '60',
    Cors__AllowedOrigins__0: 'http://localhost:3100',
  })
  api = spawn('dotnet', [path.join(OUT, 'api', 'TRL_API.dll'), '--urls', `http://localhost:${PORT}`], {
    cwd: path.join(OUT, 'api'), env: apiEnv, stdio: ['ignore', 'ignore', 'pipe'],
  })
  let apiErr = ''
  api.stderr.on('data', (d) => { apiErr += d })
  for (let i = 0; ; i++) {
    try {
      const r = await fetch(`${BASE}/Rent/GetTenants`)
      if (r.status === 401) break
    } catch { /* not up yet */ }
    if (api.exitCode !== null || i > 60) throw new Error(`API did not start on port ${PORT}. ${apiErr.slice(0, 500)}`)
    await new Promise((r) => setTimeout(r, 1000))
  }
  log('API started')

  // 4. Areas share two logged-in sessions (logins are rate limited to 10 a minute per address)
  const ctx = { a: new Session(CLIENT.a, password), b: new Session(CLIENT.b, password), password }
  await ctx.a.login()
  await ctx.b.login()
  for (const name of areas) {
    const mod = await import(pathToFileURL(path.join(E2E_DIR, 'tests', `${name}.mjs`)).href)
    try {
      await mod.default(ctx)
    } catch (e) {
      results.fail++
      results.failures.push(`[${name}] crashed: ${e.message}`)
      console.log(`    CRASH ${e.stack}`)
    }
  }
}

const started = Date.now()
let exitCode = 0
try {
  await main()
} catch (e) {
  exitCode = 1
  console.error(e instanceof SafetyError ? `\nSAFETY CHECK FAILED - nothing was changed: ${e.message}` : `\nSETUP FAILED: ${e.message}`)
} finally {
  stopApi()
  if (!teardownOnly) {
    if (keep) log(`--keep: databases left in place (${Object.values(DB).join(', ')}); remove with: node e2e/run.mjs --teardown`)
    else {
      await new Promise((r) => setTimeout(r, 1000)) // let the API release its connections
      try { teardown() } catch (e) { console.error(`Teardown failed: ${e.message}`); exitCode = 1 }
    }
  }
}
if (!teardownOnly && existsSync(OUT)) {
  console.log(`\n${results.pass} passed, ${results.fail} failed  (${Math.round((Date.now() - started) / 1000)}s)`)
  results.failures.forEach((f) => console.log(`  FAILED: ${f}`))
  results.known.forEach((k) => console.log(`  KNOWN ISSUE (not counted as a failure): ${k}`))
}
process.exit(exitCode || (results.fail ? 1 : 0))
