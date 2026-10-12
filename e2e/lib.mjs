// Shared helpers for the TRL end-to-end suite: configuration, safety guards, SQL, HTTP and assertions.
// No npm packages: Node 18+ (fetch) and sqlcmd only.
import { execFileSync } from 'node:child_process'
import { randomBytes } from 'node:crypto'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

export const E2E_DIR = path.dirname(fileURLToPath(import.meta.url))
export const REPO = path.resolve(E2E_DIR, '..')
export const OUT = path.join(E2E_DIR, '.out')

export const SQL_SERVER = process.env.TRL_E2E_SQL || 'localhost\\SQLEXPRESS'
export const PORT = Number(process.env.TRL_E2E_PORT || 5199)
export const BASE = `http://localhost:${PORT}/api`

// Every database the suite creates, reads or drops. Nothing else is ever touched.
export const DB = {
  catalog: 'TRL_E2E_Catalog_tmp',
  a: 'TRL_E2E_ClientA_tmp',
  b: 'TRL_E2E_ClientB_tmp',
}
export const CLIENT = { a: 'E2EA', b: 'E2EB' }
export const ADMIN_USER = 'e2eadmin'

// ---------------------------------------------------------------- safety (fail closed)

export class SafetyError extends Error {}

// Only throwaway E2E databases: TRL_E2E_ prefix AND _tmp suffix, and one of the names above
export const assertSafeDb = (name) => {
  if (typeof name !== 'string' || !/^TRL_E2E_[A-Za-z0-9_]+_tmp$/.test(name) || !Object.values(DB).includes(name)) {
    throw new SafetyError(`Refusing to use database "${name}": only the E2E *_tmp databases are allowed.`)
  }
  return name
}

export const connectionString = (db) =>
  `Server=${SQL_SERVER};Database=${assertSafeDb(db)};Trusted_Connection=True;TrustServerCertificate=True`

// Environment for TRL_Tools and the API: appsettings.json points to the real catalog / TRL_DB, so every connection
// is overridden here, and checked again before anything runs.
export const childEnv = (extra = {}) => {
  const env = {
    ...process.env,
    ConnectionStrings__Catalog: connectionString(DB.catalog),
    [`ClientConnections__${CLIENT.a}`]: connectionString(DB.a),
    [`ClientConnections__${CLIENT.b}`]: connectionString(DB.b),
    ClientConnections__TRL: '', // the real client's connection is blanked out
    ...extra,
  }
  assertSafeEnv(env)
  return env
}

export const assertSafeEnv = (env) => {
  const catalog = env.ConnectionStrings__Catalog || ''
  if (!catalog.includes(`Database=${DB.catalog};`)) throw new SafetyError('Catalog connection is not the E2E catalog.')
  for (const [k, v] of Object.entries(env)) {
    if (!/^(ConnectionStrings__|ClientConnections__)/i.test(k) || !v) continue
    const m = /Database=([^;]+)/i.exec(v)
    if (!m) throw new SafetyError(`${k} has no database name.`)
    assertSafeDb(m[1])
  }
}

// Runs before anything else: the guards themselves must reject the real databases, or the suite stops
export const selfCheck = () => {
  const mustThrow = (fn, what) => {
    try { fn() } catch (e) { if (e instanceof SafetyError) return; throw e }
    throw new SafetyError(`Safety self-check failed: ${what} was accepted.`)
  }
  for (const real of ['TRL_DB', 'TRL_Catalog', 'master', 'TRL_E2E_ClientA', 'TRL_DB_tmp', 'TRL_E2E_Other_tmp', '', null]) {
    mustThrow(() => assertSafeDb(real), `database "${real}"`)
  }
  mustThrow(() => assertSafeEnv({ ConnectionStrings__Catalog: 'Server=x;Database=TRL_Catalog;' }), 'the real catalog')
  mustThrow(() => assertSafeEnv({ ConnectionStrings__Catalog: `Server=x;Database=${DB.catalog};`, ClientConnections__TRL: 'Server=x;Database=TRL_DB;' }), 'a TRL_DB client connection')
  Object.values(DB).forEach(assertSafeDb)
}

export const newPassword = () => `E2e-${randomBytes(9).toString('base64url')}7a`

// ---------------------------------------------------------------- SQL (sqlcmd, Windows authentication)

const sqlcmd = (args) =>
  execFileSync('sqlcmd', ['-S', SQL_SERVER, '-E', '-I', '-f', '65001', '-b', ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] })

// Rows of a query as arrays of strings (sqlcmd text output, "~" separated)
export const sql = (db, query) =>
  sqlcmd(['-d', assertSafeDb(db), '-h', '-1', '-W', '-s', '~', '-Q', `SET NOCOUNT ON; ${query}`])
    .replace(/^﻿/, '')
    .split(/\r?\n/)
    .filter((l) => l.trim() !== '')
    .map((l) => l.split('~'))

export const one = (db, query) => sql(db, query)[0]?.[0]
export const num = (db, query) => Number(one(db, query))
export const sqlFile = (db, file) => sqlcmd(['-d', assertSafeDb(db), '-i', file])

// Server-level statements (CREATE / DROP DATABASE): only for the E2E databases
export const createDatabase = (db) => sqlcmd(['-d', 'master', '-Q', `CREATE DATABASE [${assertSafeDb(db)}]`])
export const dropDatabase = (db) =>
  sqlcmd(['-d', 'master', '-Q',
    `IF DB_ID(N'${assertSafeDb(db)}') IS NOT NULL BEGIN ALTER DATABASE [${db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${db}]; END`])
export const databaseExists = (db) =>
  sqlcmd(['-d', 'master', '-h', '-1', '-W', '-Q', `SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'${assertSafeDb(db)}') IS NULL THEN 0 ELSE 1 END`]).trim().endsWith('1')

// ---------------------------------------------------------------- HTTP

export class Session {
  constructor(clientCode, password) {
    this.clientCode = clientCode
    this.password = password
    this.cookie = ''
  }

  keepCookies(res) {
    const set = res.headers.getSetCookie?.() || []
    if (!set.length) return
    const jar = new Map(this.cookie.split('; ').filter(Boolean).map((c) => c.split(/=(.*)/s).slice(0, 2)))
    set.forEach((c) => {
      const [pair] = c.split(';')
      const [k, v] = pair.split(/=(.*)/s)
      if (v === '' || /expires=Thu, 01 Jan 1970/i.test(c)) jar.delete(k)
      else jar.set(k, v)
    })
    this.cookie = [...jar].map(([k, v]) => `${k}=${v}`).join('; ')
  }

  async login(password = this.password, username = ADMIN_USER) {
    const r = await this.call('POST', '/Auth/login', { clientCode: this.clientCode, username, password })
    if (r.status !== 200) throw new Error(`Login ${this.clientCode} failed: ${r.status} ${JSON.stringify(r.json)}`)
    return r
  }

  // { status, json }. Sends the CSRF header unless xrw = false; cookies unless anonymous.
  async call(method, url, body, { xrw = true, anonymous = false, raw = false } = {}) {
    const headers = {}
    if (body !== undefined) headers['Content-Type'] = 'application/json'
    if (xrw) headers['X-Requested-With'] = 'XMLHttpRequest'
    if (!anonymous && this.cookie) headers.Cookie = this.cookie
    const res = await fetch(`${BASE}${url}`, { method, headers, body: body === undefined ? undefined : raw ? body : JSON.stringify(body) })
    if (!anonymous) this.keepCookies(res)
    const text = await res.text()
    let json = null
    try { json = text ? JSON.parse(text) : null } catch { json = text }
    return { status: res.status, json }
  }

  get(url, opts) { return this.call('GET', url, undefined, opts) }
  post(url, body, opts) { return this.call('POST', url, body, opts) }
  put(url, body, opts) { return this.call('PUT', url, body, opts) }
  patch(url, body, opts) { return this.call('PATCH', url, body, opts) }
  del(url, opts) { return this.call('DELETE', url, undefined, opts) }
}

export const errorText = (r) => (r?.json && (r.json.errorMessage || r.json.message)) || ''

// ---------------------------------------------------------------- assertions

export const results = { pass: 0, fail: 0, failures: [] }
let currentArea = ''
export const area = (name) => {
  currentArea = name
  console.log(`\n== ${name}`)
}
export const section = (name) => console.log(`  -- ${name}`)
export const ok = (cond, name, detail) => {
  if (cond) {
    results.pass++
    console.log(`    ok    ${name}`)
  } else {
    results.fail++
    results.failures.push(`[${currentArea}] ${name}`)
    console.log(`    FAIL  ${name}${detail !== undefined ? `  ${JSON.stringify(detail)?.slice(0, 600)}` : ''}`)
  }
  return !!cond
}

// A confirmed gap in the application (not the test): reported in the summary, does not fail the run
export const known = (name, stillThere, detail) => {
  if (stillThere) {
    results.known.push(`[${currentArea}] ${name}`)
    console.log(`    KNOWN ${name}${detail !== undefined ? `  ${JSON.stringify(detail)?.slice(0, 300)}` : ''}`)
  } else {
    console.log(`    fixed ${name} (the known issue no longer happens; update the test)`)
  }
}
results.known = []

// ---------------------------------------------------------------- dates

const pad = (n) => String(n).padStart(2, '0')
export const ymd = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
// Business date (Pakistan, UTC+5): the same rule as dbo.BusinessToday() and Helpers/BusinessDate.cs
const biz = new Date(Date.now() + 5 * 3600 * 1000)
export const TODAY = `${biz.getUTCFullYear()}-${pad(biz.getUTCMonth() + 1)}-${pad(biz.getUTCDate())}`
export const addDays = (s, n) => {
  const [y, m, d] = s.split('-').map(Number)
  return ymd(new Date(y, m - 1, d + n))
}
export const monthStart = (s, addMonths = 0) => {
  const [y, m] = s.split('-').map(Number)
  return ymd(new Date(y, m - 1 + addMonths, 1))
}

// ---------------------------------------------------------------- seed data (client A database)

let seq = 0
const q = (s) => `N'${String(s).replace(/'/g, "''")}'`

// One building/floor, units, tenants, leases and invoices inserted directly (for scenarios the UI can't create,
// e.g. invoices months in the past). Returns the new ids. Invoices: [{ tag, tenant, unit, lease, month, due,
// amount, chargeType, lateFeePerDay }] where tenant/unit/lease are indexes into the arrays.
export const seed = (db, { units = 1, tenants = [], leases = [], invoices = [] }) => {
  const n = ++seq
  const lines = [
    'SET XACT_ABORT ON; BEGIN TRAN;',
    'DECLARE @B INT, @F INT; DECLARE @U TABLE (i INT, id INT); DECLARE @T TABLE (i INT, id INT); DECLARE @L TABLE (i INT, id INT); DECLARE @I TABLE (tag NVARCHAR(20), id INT);',
    `INSERT INTO Buildings (BuildingName, CityId, TypeId) VALUES (${q(`E2E Seed ${n}-${Date.now()}`)}, 1, 1); SET @B = SCOPE_IDENTITY();`,
    `INSERT INTO Floors (BuildingId, FloorNumber) VALUES (@B, N'1'); SET @F = SCOPE_IDENTITY();`,
  ]
  for (let i = 0; i < units; i++) {
    lines.push(`INSERT INTO Units (FloorId, BuildingId, UnitNumber, BaseRent) VALUES (@F, @B, ${q(`S${n}-${i + 1}`)}, 50000); INSERT INTO @U VALUES (${i}, SCOPE_IDENTITY());`)
  }
  tenants.forEach((t, i) => {
    lines.push(`INSERT INTO Tenants (Name, UnitId, MonthlyRent, Contact) VALUES (${q(t.name)}, (SELECT id FROM @U WHERE i = ${t.unit ?? 0}), ${t.rent ?? 50000}, N'03000000000'); INSERT INTO @T VALUES (${i}, SCOPE_IDENTITY());`)
  })
  leases.forEach((l, i) => {
    lines.push(`INSERT INTO TenantLeases (TenantId, UnitId, RentAmount, StartDate, TenureMonths, BilledThrough) VALUES ((SELECT id FROM @T WHERE i = ${l.tenant ?? 0}), (SELECT id FROM @U WHERE i = ${l.unit ?? 0}), ${l.rent ?? 50000}, '${l.start}', ${l.tenure ?? 12}, ${l.billedThrough ? `'${l.billedThrough}'` : 'NULL'}); INSERT INTO @L VALUES (${i}, SCOPE_IDENTITY());`)
  })
  invoices.forEach((v) => {
    const lease = v.chargeType || v.lease === null ? 'NULL' : `(SELECT id FROM @L WHERE i = ${v.lease ?? 0})`
    lines.push(`INSERT INTO RentInvoices (TenantId, LeaseId, UnitId, TotalRent, PendingAmount, OverPaidAmount, InvoiceDate, DueDate, StatusId, ChargeType, Description, CreatedAt, LateFeePerDay, LateFeeMaxMultiplier)
      VALUES ((SELECT id FROM @T WHERE i = ${v.tenant ?? 0}), ${lease}, (SELECT id FROM @U WHERE i = ${v.unit ?? 0}), ${v.amount ?? 50000}, ${v.amount ?? 50000}, 0, '${v.month}', '${v.due}', 2,
              ${v.chargeType ? q(v.chargeType) : 'NULL'}, ${v.chargeType ? q('E2E charge') : 'NULL'}, GETDATE(), ${v.lateFeePerDay ?? 500}, 2);
      INSERT INTO @I VALUES (${q(v.tag)}, SCOPE_IDENTITY());`)
  })
  lines.push('COMMIT;')
  lines.push(`SELECT 'B', @B UNION ALL SELECT 'F', @F UNION ALL SELECT CONCAT('U', i), id FROM @U UNION ALL SELECT CONCAT('T', i), id FROM @T
              UNION ALL SELECT CONCAT('L', i), id FROM @L UNION ALL SELECT CONCAT('I:', tag), id FROM @I;`)
  const ids = { units: [], tenants: [], leases: [], inv: {} }
  sql(db, lines.join('\n')).forEach(([k, v]) => {
    const id = Number(v)
    if (k === 'B') ids.building = id
    else if (k === 'F') ids.floor = id
    else if (k.startsWith('I:')) ids.inv[k.slice(2)] = id
    else ({ U: ids.units, T: ids.tenants, L: ids.leases })[k[0]][Number(k.slice(1))] = id
  })
  return ids
}

// A complete tenancy created through the API, the way the screens do it: building, floor, unit, tenant, lease.
// Returns { buildingId, floorId, unitId, tenantId, leaseId, name }.
export const apiTenancy = async (s, db, { start, rent = 40000, tenure = 12, units = 1 } = {}) => {
  const n = `${++seq}-${Date.now() % 1e7}`
  const must = (r, what) => {
    if (r.status !== 200 || r.json?.isSuccess === false) throw new Error(`${what}: ${r.status} ${JSON.stringify(r.json)}`)
    return r.json
  }
  const buildingId = must(await s.post('/Properties/SaveBuilding', { buildingName: `E2E Tower ${n}`, cityId: 1, typeId: 1, address: 'E2E street' }), 'SaveBuilding').id
  const floorId = must(await s.post('/Properties/SaveFloor', { buildingId, floorNumber: '1' }), 'SaveFloor').id
  const unitIds = []
  for (let i = 1; i <= units; i++) {
    unitIds.push(must(await s.post('/Properties/SaveUnit', { buildingId, floorId, unitNumber: `U${i}`, statusId: 1, baseRent: rent }), 'SaveUnit').id)
  }
  const name = `E2E Tenant ${n}`
  must(await s.post('/Tenant/Create', { name, tenantType: 'Individual', contact: '0300-1234567', cnicNtn: '42101-1234567-1', cityId: 1, unitId: unitIds[0], buildingId, floorId, monthlyRent: rent }), 'Tenant/Create')
  const tenantId = num(db, `SELECT MAX(TenantId) FROM Tenants WHERE Name = N'${name}'`)
  let leaseId = null
  if (start) {
    must(await s.post('/Lease/Create', { tenantId, unitId: unitIds[0], rentAmount: rent, startDate: start, tenureMonths: tenure }), 'Lease/Create')
    leaseId = num(db, `SELECT MAX(LeaseId) FROM TenantLeases WHERE TenantId = ${tenantId}`)
  }
  return { buildingId, floorId, unitId: unitIds[0], unitIds, tenantId, leaseId, name }
}

// Invoice balance and cached status, straight from the database (dbo.InvoiceBalance)
export const balanceOf = (db, invoiceId) => {
  const [r] = sql(db, `SELECT ri.StatusId, b.Paid, b.Disc, b.Waived, b.Balance, b.OpenLateFee, ri.PendingAmount, ri.OverPaidAmount
                       FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b WHERE ri.Id = ${Number(invoiceId)}`)
  const n = r.map(Number)
  return { status: n[0], paid: n[1], disc: n[2], waived: n[3], balance: n[4], openFee: n[5], pending: n[6], overpaid: n[7] }
}

// The late fee that applies today on an invoice's unpaid rent (independent of the API)
export const expectedLateFee = (db, invoiceId) =>
  num(db, `SELECT dbo.CalculateLateFee(ri.TotalRent - b.Paid - b.Disc, ri.TotalRent, ri.DueDate, dbo.BusinessToday(), ri.LateFeePerDay, ri.LateFeeMaxMultiplier)
           FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b WHERE ri.Id = ${Number(invoiceId)}`)

// Every column of a payment row, to prove it was never changed
export const paymentRow = (db, id) =>
  sql(db, `SELECT Id, TenantId, PaymentAmount, CONVERT(varchar(30), PaymentDate, 126), RentInvoiceId, PaymentMethod, Notes, DiscountAmount,
                  DiscountPercent, IsLateFeeWaived, CreatedBy, CONVERT(varchar(30), CreatedAt, 126), UpdatedBy, CONVERT(varchar(30), UpdatedAt, 126), ReversalOfPaymentId
           FROM Payments WHERE Id = ${Number(id)}`)[0]?.join('|')
