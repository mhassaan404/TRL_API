// Staff users and permissions: the Super Admin creates a user and ticks pages (view / edit) and risky actions; the
// server checks them on every request (a change applies to the user's next action, no new login needed); switching a
// user off or resetting their password signs them out; only the Super Admin manages users.
import { ADMIN_USER, CLIENT, DB, Session, area, errorText, newPassword, num, ok, section, sql } from '../lib.mjs'

const STAFF = 'e2e.staff'
const tenantBody = (name) => ({ name, tenantType: 'Individual', contact: '0300-1234567', cnicNtn: '42101-1234567-1', cityId: 1 })

export default async (ctx) => {
  area('permissions')
  const { a } = ctx
  const pass1 = newPassword()
  const pass2 = newPassword()
  const staff = new Session(CLIENT.a)
  const idOf = async () => (await a.get('/Users/GetAll')).json.find((u) => u.username === STAFF)?.userId
  const setAccess = (userId, permissions, isActive = true) =>
    a.post('/Users/Update', { userId, fullName: 'E2E Staff', email: 'staff@e2e.test', phone: '0300-1112223', isActive, permissions })

  section('Users page: create a staff user (Super Admin only)')
  let userId
  {
    let r = await a.get('/Users/GetAll')
    ok(r.status === 200 && r.json.some((u) => u.username === ADMIN_USER && u.isSuperAdmin), 'Super Admin listed as Super Admin', r.json)
    r = await a.post('/Users/Create', { username: 'bad name', fullName: 'X', password: pass1, confirmPassword: pass1 })
    ok(r.status === 400 && /Username/.test(errorText(r)), 'username with a space refused', r.json)
    r = await a.post('/Users/Create', { username: STAFF, fullName: '', password: pass1, confirmPassword: pass1 })
    ok(r.status === 400 && /name/.test(errorText(r)), 'missing name refused', r.json)
    r = await a.post('/Users/Create', { username: STAFF, fullName: 'E2E Staff', password: 'short1', confirmPassword: 'short1' })
    ok(r.status === 400 && /at least/.test(errorText(r)), 'weak password refused', r.json)
    r = await a.post('/Users/Create', { username: ADMIN_USER, fullName: 'Dup', password: pass1, confirmPassword: pass1 })
    ok(r.status === 400 && /taken/.test(errorText(r)), 'duplicate username refused', r.json)
    r = await a.post('/Users/Create', {
      username: STAFF, fullName: 'E2E Staff', email: 'staff@e2e.test', password: pass1, confirmPassword: pass1,
      permissions: ['tenants:view', 'tenants:edit', 'payments:view', 'bogus:edit', 'payments:edit'],
    })
    ok(r.status === 200, 'staff user created', r.json)
    userId = await idOf()
    const stored = sql(DB.a, `SELECT Permission FROM UserPermissions WHERE UserId = ${userId} ORDER BY Permission`).map((x) => x[0])
    ok(JSON.stringify(stored) === JSON.stringify(['payments:view', 'tenants:edit']), 'only known permissions stored, edit kept instead of view', stored)
    ok(num(DB.a, `SELECT COUNT(*) FROM Users WHERE UserId = ${userId} AND Role = N'User' AND IsActive = 1 AND PasswordHash NOT LIKE N'%${pass1}%'`) === 1,
      'stored as an active staff user with a hashed password')
  }

  section('staff login: sees only their pages')
  {
    let r = await staff.login(pass1, STAFF)
    ok(r.json.access?.isSuperAdmin === false && r.json.access.permissions.includes('tenants:edit'), 'login returns the access', r.json)
    r = await staff.get('/Auth/me')
    ok(r.status === 200 && r.json.fullName === 'E2E Staff' && r.json.permissions.length === 2, 'Auth/me returns name and permissions', r.json)
    r = await staff.get('/Tenant/GetTenants')
    ok(r.status === 200, 'allowed page data loads (tenants)', r.status)
    r = await staff.get('/Rent/GetAllPayments')
    ok(r.status === 200, 'allowed page data loads (payment records)', r.status)
    for (const url of ['/Reports/ArrearsAgeing', '/Rent/GetRentCollection', '/Lease/GetAll', '/LateFeeSettings/Get', '/Users/GetAll', '/Dashboard/dashboard'])
      ok((await staff.get(url)).status === 403, `not allowed: ${url} -> 403`)
    r = await staff.get('/CompanyProfile/Get')
    ok(r.status === 200, 'company name/logo for documents: any signed-in user', r.status)
    r = await staff.post('/CompanyProfile/Save', { phone: '1' })
    ok(r.status === 403 && /permission/.test(errorText(r)), 'company profile save refused with a clear message', r.json)
  }

  section('edit vs view, and risky actions')
  {
    let r = await staff.post('/Tenant/Create', tenantBody('E2E Perm Tenant'))
    ok(r.status === 200, 'tenants:edit -> can add a tenant', r.json)
    const tenantId = num(DB.a, `SELECT TenantId FROM Tenants WHERE Name = N'E2E Perm Tenant'`)
    r = await staff.del(`/Tenant/Delete?tenantId=${tenantId}`)
    ok(r.status === 403, 'delete needs the "delete or cancel" action', r.status)
    ok(num(DB.a, `SELECT COUNT(*) FROM Tenants WHERE TenantId = ${tenantId}`) === 1, 'tenant still there')

    // a change applies to the very next request (same session, no new login)
    await setAccess(userId, ['tenants:view'])
    r = await staff.post('/Tenant/Create', tenantBody('E2E Perm Tenant 2'))
    ok(r.status === 403, 'view only now: add refused on the next request', r.status)
    r = await staff.get('/Tenant/GetTenants')
    ok(r.status === 200, 'view only: list still loads', r.status)

    await setAccess(userId, ['tenants:edit', 'action:deleteCancel', 'rentCollection:edit', 'rentHistory:edit'])
    r = await staff.del(`/Tenant/Delete?tenantId=${tenantId}`)
    ok(r.status === 200, 'with "delete or cancel": delete allowed', r.json)

    const discount = [{ tenantId: 1, rentInvoiceId: 1, paymentAmount: 0, discountAmount: 100, paymentDate: '2026-01-01', paymentMethod: 'Cash' }]
    r = await staff.post('/Rent/SubmitPayments', discount)
    ok(r.status === 403 && /discount/.test(errorText(r)), 'discount without permission refused', r.json)
    const waive = [{ tenantId: 1, rentInvoiceId: 1, paymentAmount: 0, isLateFeeWaived: true, paymentDate: '2026-01-01', paymentMethod: 'Cash' }]
    r = await staff.post('/Rent/SubmitPayments', waive)
    ok(r.status === 403 && /waive/.test(errorText(r)), 'late-fee waiver without permission refused', r.json)
    r = await staff.post('/Rent/ChargeLateFee?invoiceId=1')
    ok(r.status === 403, 'charge late fee without permission refused', r.status)
    r = await staff.post('/Rent/ReversePayment', { paymentId: 1, reason: 'x' })
    ok(r.status === 403, 'reverse payment without permission refused', r.status)
    r = await staff.post('/Lease/Terminate', { leaseId: 1 })
    ok(r.status === 403, 'end lease without lease edit refused', r.status)

    await setAccess(userId, ['rentCollection:edit', 'action:discount'])
    r = await staff.post('/Rent/SubmitPayments', discount)
    ok(r.status !== 403, 'with the discount action the permission check passes (the payment itself is then checked)', r)
  }

  section('only the Super Admin manages users')
  {
    let r = await staff.post('/Users/Update', { userId, fullName: 'Me', isActive: true, permissions: ['tenants:edit'] })
    ok(r.status === 403, 'staff cannot change their own permissions', r.status)
    const adminId = num(DB.a, `SELECT UserId FROM Users WHERE Username = N'${ADMIN_USER}'`)
    r = await a.post('/Users/Update', { userId: adminId, fullName: 'X', isActive: false, permissions: [] })
    ok(r.status === 400 && num(DB.a, `SELECT COUNT(*) FROM Users WHERE UserId = ${adminId} AND IsActive = 1`) === 1, 'the Super Admin cannot be switched off', r.json)
    r = await a.post('/Users/ResetPassword', { userId: adminId, newPassword: pass2, confirmPassword: pass2 })
    ok(r.status === 400, "the Super Admin's password cannot be reset here", r.json)
  }

  section('password reset and switching off sign the user out')
  {
    let r = await staff.post('/Auth/change-password', { currentPassword: pass1, newPassword: pass2, confirmPassword: pass2 })
    ok(r.status === 200, 'staff can change their own password', r.json)
    r = await a.post('/Users/ResetPassword', { userId, newPassword: pass1, confirmPassword: pass1 })
    ok(r.status === 200, 'Super Admin resets the password', r.json)
    r = await staff.post('/Auth/refresh')
    ok(r.status === 401, 'reset signs the user out (refresh refused)', r.status)
    r = await new Session(CLIENT.a).post('/Auth/login', { clientCode: CLIENT.a, username: STAFF, password: pass2 })
    ok(r.status === 401, 'old password no longer works', r.status)
    await staff.login(pass1, STAFF)
    ok((await staff.get('/Rent/GetRentCollection')).status === 200, 'new password works')

    await setAccess(userId, ['rentCollection:edit'], false)
    r = await staff.get('/Rent/GetRentCollection')
    ok(r.status === 401, 'switched off: the next request is refused (401)', r.status)
    r = await staff.post('/Auth/refresh')
    ok(r.status === 401, 'switched off: refresh refused', r.status)
    r = await new Session(CLIENT.a).post('/Auth/login', { clientCode: CLIENT.a, username: STAFF, password: pass1 })
    ok(r.status === 403 && /deactivated/.test(errorText(r)), 'switched off: login refused', r.json)
    const list = (await a.get('/Users/GetAll')).json.find((u) => u.username === STAFF)
    ok(list && list.isActive === false && list.permissions.includes('rentCollection:edit'), 'listed as off, permissions kept', list)
  }

  section('Super Admin keeps everything')
  {
    const r = await a.get('/Auth/me')
    ok(r.status === 200 && r.json.isSuperAdmin && r.json.permissions.includes('action:reversePayment') && r.json.permissions.length > 25,
      'Auth/me: Super Admin with every permission', r.json)
    ok((await a.get('/Reports/ArrearsAgeing')).status === 200, 'Super Admin still opens everything')
  }
}
