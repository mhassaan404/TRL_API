// Login, refresh, logout, change password, Admin-only and active-user rules, CSRF, client isolation and the login
// rate limit. Runs last: it changes client A's password and uses up the login rate limit (10 a minute per address).
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { ADMIN_USER, BASE, CLIENT, DB, OUT, Session, area, errorText, newPassword, num, ok, section, sql } from '../lib.mjs'

export default async (ctx) => {
  area('auth')
  const { a, b } = ctx
  const anon = new Session('', '')
  const originalPassword = ctx.password

  section('login')
  {
    let r = await anon.post('/Auth/login', { clientCode: CLIENT.a, username: ADMIN_USER })
    ok(r.status === 400 && !anon.cookie, 'missing password -> 400', r.json)
    r = await anon.post('/Auth/login', { clientCode: CLIENT.a, username: ADMIN_USER, password: 'wrong-password-1' })
    ok(r.status === 401 && !anon.cookie, 'wrong password -> 401, no cookies', r.json)
    const wrongPasswordMessage = errorText(r)
    r = await anon.post('/Auth/login', { clientCode: 'NOPE', username: ADMIN_USER, password: ctx.password })
    ok(r.status === 401 && errorText(r) === wrongPasswordMessage, 'unknown client -> 401 with the same message (nothing revealed)', r.json)

    sql(DB.b, `UPDATE Users SET IsActive = 0 WHERE Username = N'${ADMIN_USER}'`)
    r = await new Session(CLIENT.b).post('/Auth/login', { clientCode: CLIENT.b, username: ADMIN_USER, password: ctx.password })
    ok(r.status === 403 && /deactivated/.test(errorText(r)), 'deactivated user -> 403', r.json)
    r = await b.post('/Auth/refresh')
    ok(r.status === 401, 'deactivated user: refresh refused', r.status)
    sql(DB.b, `UPDATE Users SET IsActive = 1, Role = N'Manager' WHERE Username = N'${ADMIN_USER}'`)
    r = await new Session(CLIENT.b).post('/Auth/login', { clientCode: CLIENT.b, username: ADMIN_USER, password: ctx.password })
    ok(r.status === 403 && /doesn't have access/.test(errorText(r)), 'non-Admin role -> 403', r.json)
    sql(DB.b, `UPDATE Users SET Role = N'Admin' WHERE Username = N'${ADMIN_USER}'`)
  }

  section('session: cookies, refresh, tampering, logout')
  {
    let r = await anon.get('/Rent/GetRentCollection')
    ok(r.status === 401, 'no cookie -> 401', r.status)
    const tampered = new Session(CLIENT.a)
    tampered.cookie = a.cookie.replace(/jwt=([^;]+)/, (m, v) => `jwt=${v.slice(0, -4)}AAAA`)
    r = await tampered.get('/Rent/GetRentCollection')
    ok(r.status === 401, 'tampered access token -> 401', r.status)
    const oldCookie = a.cookie
    r = await a.post('/Auth/refresh')
    ok(r.status === 200 && a.cookie !== oldCookie, 'refresh issues new cookies', r.json)
    const replay = new Session(CLIENT.a)
    replay.cookie = oldCookie
    r = await replay.post('/Auth/refresh')
    ok(r.status === 401, 'old refresh token no longer works (rotation)', r.status)
    r = await a.get('/Rent/GetRentCollection')
    ok(r.status === 200, 'session still works after refresh', r.status)
    r = await a.post('/Auth/refresh', undefined, { xrw: false })
    ok(r.status === 403, 'refresh without X-Requested-With -> 403 (CSRF)', r.status)

    const bCookie = b.cookie
    r = await b.post('/Auth/logout')
    ok(r.status === 200 && !/refreshToken=/.test(b.cookie), 'logout clears the cookies', r.json)
    const afterLogout = new Session(CLIENT.b)
    afterLogout.cookie = bCookie
    r = await afterLogout.post('/Auth/refresh')
    ok(r.status === 401, 'refresh token from before logout no longer works', r.status)
  }

  section('client isolation')
  {
    // Client A's cookies only reach client A's database: same ids, different data
    sql(DB.a, "INSERT INTO Tenants (Name, Contact) VALUES (N'E2E isolation tenant', N'03000000000')")
    const aTenants = num(DB.a, 'SELECT COUNT(*) FROM Tenants WHERE IsDeleted = 0')
    const bTenants = num(DB.b, 'SELECT COUNT(*) FROM Tenants')
    const r = await a.get('/Tenant/GetTenants')
    ok(r.status === 200 && r.json.length === aTenants && aTenants !== bTenants, `client A sees its own ${aTenants} tenants (client B has ${bTenants})`, r.json?.length)
  }

  section('change password')
  {
    const next = newPassword()
    let r = await a.post('/Auth/change-password', { currentPassword: 'wrong-1234', newPassword: next, confirmPassword: next })
    ok(r.status === 400 && /current password is incorrect/.test(errorText(r)), 'wrong current password -> 400', r.json)
    r = await a.post('/Auth/change-password', { currentPassword: ctx.password, newPassword: 'short1', confirmPassword: 'short1' })
    ok(r.status === 400, 'weak new password -> 400', r.json)
    r = await a.post('/Auth/change-password', { currentPassword: ctx.password, newPassword: next, confirmPassword: `${next}x` })
    ok(r.status === 400, 'confirmation mismatch -> 400', r.json)
    r = await a.post('/Auth/change-password', { currentPassword: ctx.password, newPassword: ctx.password, confirmPassword: ctx.password })
    ok(r.status === 400 && /different/.test(errorText(r)), 'same password -> 400', r.json)
    const otherDevice = new Session(CLIENT.a)
    otherDevice.cookie = a.cookie
    r = await a.post('/Auth/change-password', { currentPassword: ctx.password, newPassword: next, confirmPassword: next })
    ok(r.status === 200, 'password changed', r.json)
    ok(num(DB.a, `SELECT COUNT(*) FROM RefreshTokens rt JOIN Users u ON u.UserId = rt.UserId WHERE u.Username = N'${ADMIN_USER}'`) === 1, 'other sessions signed out (one refresh token left: this browser)')
    r = await otherDevice.post('/Auth/refresh')
    ok(r.status === 401, "another device's refresh token stops working", r.status)
    r = await a.get('/Rent/GetRentCollection')
    ok(r.status === 200, 'this browser stays signed in', r.status)
    r = await new Session(CLIENT.a).post('/Auth/login', { clientCode: CLIENT.a, username: ADMIN_USER, password: ctx.password })
    ok(r.status === 401, 'old password no longer works', r.status)
    r = await new Session(CLIENT.a).post('/Auth/login', { clientCode: CLIENT.a, username: ADMIN_USER, password: next })
    ok(r.status === 200, 'new password works', r.json)
    ctx.password = next
  }

  section('security headers, health check, security log')
  {
    const wantHeaders = (h) => h.get('x-content-type-options') === 'nosniff' && h.get('x-frame-options') === 'DENY'
      && h.get('referrer-policy') === 'no-referrer' && /frame-ancestors 'none'/.test(h.get('content-security-policy') || '')
      && h.get('cache-control') === 'no-store'
    let raw = await fetch(`${BASE}/Rent/GetTenants`)
    ok(raw.status === 401 && wantHeaders(raw.headers), 'security headers on an error response (401)', Object.fromEntries(raw.headers))
    raw = await fetch(`${BASE}/Lease/GetAll`, { headers: { Cookie: a.cookie } })
    ok(raw.status === 200 && wantHeaders(raw.headers), 'security headers + no-store on tenant/lease data', Object.fromEntries(raw.headers))

    raw = await fetch(`${BASE}/health`)
    const health = await raw.json()
    ok(raw.status === 200 && health.status === 'ok' && Object.keys(health).length === 1 && wantHeaders(raw.headers),
      'health check: 200 {"status":"ok"} without login, nothing else revealed', health)

    // The API writes warnings to e2e/.out/api/logs (its content root); everything above left security events there
    const dir = path.join(OUT, 'api', 'logs')
    const log = existsSync(dir)
      ? readdirSync(dir).filter((f) => /^trl-\d{4}-\d{2}-\d{2}\.log$/.test(f)).map((f) => readFileSync(path.join(dir, f), 'utf8')).join('\n')
      : ''
    ok(new RegExp(`Login failed for ${CLIENT.a}/${ADMIN_USER} from .*wrong username or password`).test(log), 'failed login is in the log file', log.slice(-400))
    ok(new RegExp(`Login failed for NOPE/${ADMIN_USER} from .*unknown or inactive client code`).test(log), 'unknown client code is in the log file')
    ok(new RegExp(`Login refused for ${CLIENT.b}/${ADMIN_USER} from .*deactivated`).test(log)
      && new RegExp(`Login refused for ${CLIENT.b}/${ADMIN_USER} from .*role Manager has no access`).test(log), 'deactivated and non-Admin logins are in the log file')
    ok(new RegExp(`Session refresh refused for client ${CLIENT.a} from .*refresh token`).test(log), 'refused refresh token is in the log file')
    ok(new RegExp(`Password change failed for ${CLIENT.a}/${ADMIN_USER} .*wrong current password`).test(log)
      && new RegExp(`Password changed for ${CLIENT.a}/${ADMIN_USER} `).test(log), 'password change events are in the log file')
    const secrets = ['wrong-password-1', 'wrong-1234', originalPassword, ctx.password]
    ok(log.length > 0 && !secrets.some((s) => log.includes(s)) && !/eyJ[A-Za-z0-9_-]{20,}/.test(log) && !/refreshToken=/.test(log),
      'no password, refresh token or JWT in the log file')
  }

  section('login rate limit')
  {
    const codes = []
    for (let i = 0; i < 21; i++) codes.push((await anon.post('/Auth/login', { clientCode: CLIENT.a })).status)
    ok(codes.includes(429), 'too many login requests from one address -> 429', codes)
  }
}
