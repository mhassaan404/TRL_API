// Security deposits (agreed, received, corrections, receipts) and move-out settlement (finalize, deductions,
// deposit applied to dues, final payment, refunds), checked against the database
import { ADMIN_USER, DB, TODAY, apiTenancy, area, balanceOf, errorText, monthStart, num, ok, one, section, sql } from '../lib.mjs'

const parts = (d) => ({ month: Number(d.slice(5, 7)), year: Number(d.slice(0, 4)) })
const round2 = (v) => Math.round(v * 100) / 100

export default async ({ a }) => {
  area('deposits-settlement')
  const db = DB.a
  const held = (t) => num(db, `SELECT ISNULL(SUM(Amount), 0) FROM SecurityDeposits WHERE TenantId = ${t.tenantId} AND UnitId = ${t.unitId}`)
  const record = (t, amount, extra = {}) => a.post('/SecurityDeposits/Record', { leaseId: t.leaseId, amount, entryDate: TODAY, paymentMethod: 'Cash', reference: 'E2E', ...extra })
  const openBalance = (t) => num(db, `SELECT ISNULL(SUM(b.Balance), 0) FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                                      WHERE ri.TenantId = ${t.tenantId} AND ri.StatusId <> 6`)

  // ---- tenancy 1: deposit smaller than what is owed
  const t1 = await apiTenancy(a, db, { start: monthStart(TODAY, -1), rent: 40000 })

  section('security deposit')
  {
    let r = await a.post('/SecurityDeposits/SetAgreed', { tenantId: t1.tenantId, unitId: t1.unitId, agreedAmount: 30000 })
    ok(r.status === 200, 'agreed deposit set', r.json)
    r = await record(t1, 0)
    ok(r.status === 400, 'zero deposit refused', r.json)
    r = await record(t1, 20000, { paymentMethod: 'Gold' })
    ok(r.status === 400, 'unknown payment method refused', r.json)
    r = await record(t1, 20000)
    ok(r.status === 200 && held(t1) === 20000, 'deposit received (20,000)', r.json)
    const depId = num(db, `SELECT MAX(Id) FROM SecurityDeposits WHERE TenantId = ${t1.tenantId}`)
    r = await record(t1, 15000)
    ok(r.status === 400 && /agreed/i.test(errorText(r)) && held(t1) === 20000, 'receiving more than the agreed deposit refused', r.json)
    r = await record(t1, 5000)
    const r2 = await record(t1, 5000)
    ok(r.status === 200 && r2.status === 400 && /a moment ago/.test(errorText(r2)) && held(t1) === 25000, 'same entry twice within a minute refused', r2.json)
    r = await a.post('/SecurityDeposits/Correct', { tenantId: t1.tenantId, unitId: t1.unitId, amount: 99999, entryDate: TODAY, reason: 'too much' })
    ok(r.status === 400 && /more than the deposit held/.test(errorText(r)), 'correction above what is held refused', r.json)
    r = await a.post('/SecurityDeposits/Correct', { tenantId: t1.tenantId, unitId: t1.unitId, amount: 5000, entryDate: TODAY, reason: '' })
    ok(r.status === 400, 'correction without a reason refused', r.json)
    r = await a.post('/SecurityDeposits/Correct', { tenantId: t1.tenantId, unitId: t1.unitId, amount: 5000, entryDate: TODAY, reason: 'E2E entered twice' })
    ok(r.status === 200 && held(t1) === 20000, 'correction takes 5,000 off (held 20,000)', r.json)
    ok(one(db, `SELECT TOP 1 CONCAT(EntryType, '|', Amount) FROM SecurityDeposits WHERE TenantId = ${t1.tenantId} ORDER BY Id DESC`) === 'Correction|-5000.00', 'correction stored as a negative entry, nothing deleted')
    r = await a.post('/SecurityDeposits/SetAgreed', { tenantId: t1.tenantId, unitId: t1.unitId, agreedAmount: 10000 })
    ok(r.status === 400 && /less than the deposit already held/.test(errorText(r)), 'agreed below held refused', r.json)
    r = await a.get(`/SecurityDeposits/Receipt?depositId=${depId}`)
    ok(r.status === 200, 'deposit receipt available', r.status)
    const row = ((await a.get('/SecurityDeposits/GetAll')).json || []).find((x) => x.tenantId === t1.tenantId)
    ok(row && Number(row.held) === 20000 && Number(row.agreedAmount) === 30000, 'Security Deposits list shows held / agreed', row)
    ok(num(db, `SELECT COUNT(*) FROM Payments WHERE TenantId = ${t1.tenantId}`) === 0, 'deposits never create rent payments')
  }

  section('settlement: deposit smaller than the dues')
  let s1
  {
    for (const d of [monthStart(TODAY, -1), TODAY]) await a.post('/Rent/GenerateInvoices', { tenantIds: [t1.tenantId], ...parts(d) })
    let r = await a.post('/Settlements/Finalize', { tenantId: t1.tenantId, unitId: t1.unitId, leaseId: t1.leaseId, expectedOutstanding: 0, expectedCredit: 0, expectedHeld: 20000, settlementDate: TODAY })
    ok(r.status === 400 && /current or upcoming lease/.test(errorText(r)), 'cannot settle while the lease is current', r.json)
    // "Current / Moved out" flag on Rent History and Payment Records rows (per tenant + unit)
    const historyOf = async (t) => ((await a.get('/RentHistory/History')).json || []).filter((x) => x.tenant === t.name)
    let mine = await historyOf(t1)
    ok(mine.length === 2 && mine.every((x) => x.leaseCurrent === true), 'Rent History: invoices of a current tenancy are marked current', mine.map((x) => x.leaseCurrent))
    r = await a.post('/Lease/Terminate', { leaseId: t1.leaseId, reason: 'E2E move-out', moveOutDate: TODAY })
    ok(r.status === 200, 'lease ended', r.json)
    mine = await historyOf(t1)
    ok(mine.length === 2 && mine.every((x) => x.leaseCurrent === false), 'Rent History: after the lease ends they are marked moved out', mine.map((x) => x.leaseCurrent))
    r = await record(t1, 1000)
    ok(r.status === 400 && /ended or was cancelled/.test(errorText(r)), 'no new deposit on an ended lease', r.json)
    // A charge cancelled before the settlement (used below: it must not come back afterwards)
    await a.post('/Rent/CreateExtraCharge', { tenantIds: [t1.tenantId], chargeType: 'Other', description: 'E2E cancelled before settlement', amount: 100 })
    const cancelledCharge = num(db, `SELECT MAX(Id) FROM RentInvoices WHERE TenantId = ${t1.tenantId} AND ChargeType = N'Other'`)
    await a.patch('/RentHistory/CancelInvoice', { invoiceId: cancelledCharge, reason: 'e2e' })
    const pv = (await a.get(`/Settlements/Preview?tenantId=${t1.tenantId}&unitId=${t1.unitId}`)).json
    const outstanding = openBalance(t1)
    const pvOutstanding = round2((pv?.invoices || []).filter((i) => Number(i.balance) > 0).reduce((s, i) => s + Number(i.balance), 0))
    ok(pvOutstanding === outstanding && Number(pv?.header?.held) === 20000 && pv?.header?.hasCurrentLease === false && (pv?.unbilled || []).length === 0,
       `preview: open invoices ${outstanding}, held 20,000, lease ended, nothing unbilled`, { pvOutstanding, header: pv?.header })
    const body = {
      tenantId: t1.tenantId, unitId: t1.unitId, leaseId: t1.leaseId, expectedOutstanding: outstanding, expectedCredit: 0, expectedHeld: 20000,
      settlementDate: TODAY, deductions: [{ chargeType: 'Cleaning', amount: 2000, reason: 'E2E cleaning' }], acknowledgeUnbilled: true,
    }
    r = await a.post('/Settlements/Finalize', { ...body, expectedOutstanding: outstanding + 1 })
    ok(r.status === 400 && /changed after you opened/.test(errorText(r)), 'figures changed since the preview: refused', r.json)
    const owes = round2(outstanding + 2000 - 20000)
    r = await a.post('/Settlements/Finalize', { ...body, finalPayment: { amount: owes + 1, paymentMethod: 'Cash' } })
    ok(r.status === 400 && /can't be more than what the tenant owes/.test(errorText(r)), 'final payment above what is owed refused', r.json)
    r = await a.post('/Settlements/Finalize', { ...body, finalPayment: { amount: 5000, paymentMethod: 'Cash', reference: 'E2E' } })
    s1 = r.json?.id
    ok(num(db, `SELECT COUNT(*) FROM RentInvoices ri JOIN Users u ON u.UserId = ri.CreatedBy WHERE ri.TenantId = ${t1.tenantId} AND ri.ChargeType = N'Cleaning' AND u.Username = N'${ADMIN_USER}'`) === 1,
       'settlement deduction invoice records who created it')
    ok(r.status === 200 && s1 > 0 && r.json.receiptIds?.length === 1, 'settlement finalized with a deduction and a final payment', r.json)
    const pays = ((await a.get(`/Rent/GetAllPayments?from=${monthStart(TODAY, -2)}&to=${TODAY}`)).json || []).filter((x) => x.tenantId === t1.tenantId)
    ok(pays.length > 0 && pays.every((x) => x.leaseCurrent === false), 'Payment Records: payments of a moved-out tenancy are marked moved out', pays.map((x) => x.leaseCurrent))
    ok((await historyOf(t1)).every((x) => x.leaseCurrent === false), 'Rent History: the settlement deduction invoice is marked moved out too')
    const [st] = sql(db, `SELECT OutstandingBefore, DeductionsTotal, DepositHeldBefore, DepositApplied, TenantOwes, RefundDue, FinalPaymentReceived FROM MoveOutSettlements WHERE Id = ${s1}`)
    ok(st.map(Number).join('|') === [outstanding, 2000, 20000, 20000, owes, 0, 5000].join('|'), 'settlement figures: owed = dues + deductions - deposit', st)
    ok(held(t1) === 0, 'deposit fully applied (held 0)')
    ok(num(db, `SELECT SUM(PaymentAmount) FROM Payments WHERE TenantId = ${t1.tenantId} AND PaymentMethod = N'Security Deposit'`) === 20000, 'deposit applied as "Security Deposit" payments (20,000)')
    const ded = num(db, `SELECT MAX(Id) FROM RentInvoices WHERE TenantId = ${t1.tenantId} AND ChargeType = N'Cleaning'`)
    ok(ded > 0 && num(db, `SELECT LateFeePerDay FROM RentInvoices WHERE Id = ${ded}`) === 0, 'deduction billed as its own invoice without late fee')
    ok(openBalance(t1) === round2(owes - 5000), `tenant still owes ${owes - 5000} on the invoices`, openBalance(t1))
    const coll = (await a.get(`/Reports/Collections?from=${TODAY}&to=${TODAY}`)).json
    ok(!coll.some((x) => x.method === 'Security Deposit') && coll.some((x) => x.paymentId === r.json.receiptIds[0] && x.amount === 5000),
       'Collections: final payment counted, deposit application not counted as cash')
    r = await a.post('/Settlements/Finalize', { ...body, finalPayment: null })
    ok(r.status === 400 && /already been settled/.test(errorText(r)), 'second settlement refused', r.json)
    const depPay = num(db, `SELECT MAX(Id) FROM Payments WHERE TenantId = ${t1.tenantId} AND PaymentMethod = N'Security Deposit'`)
    r = await a.post('/Rent/ReversePayment', { paymentId: depPay, reason: 'e2e' })
    ok(r.status === 400 && /settlement/.test(errorText(r)), 'settlement payments cannot be reversed', r.json)
    const finalPay = num(db, `SELECT MAX(CashPaymentId) FROM MoveOutSettlementLines WHERE SettlementId = ${s1}`)
    r = await a.post('/Rent/ReversePayment', { paymentId: finalPay, reason: 'e2e' })
    ok(r.status === 400 && /settlement/.test(errorText(r)), 'final payment taken at settlement cannot be reversed', r.json)
    r = await a.post('/Settlements/RecordRefund', { settlementId: s1, amount: 100, refundDate: TODAY, paymentMethod: 'Cash' })
    ok(r.status === 400 && /Nothing is left to refund/.test(errorText(r)), 'refund refused when nothing is refundable', r.json)
    // A finalized settlement fixes its invoices: no late fee, no cancelling (a deduction left unpaid included)
    const owedInv = num(db, `SELECT TOP 1 msl.InvoiceId FROM MoveOutSettlementLines msl CROSS APPLY dbo.InvoiceBalance(msl.InvoiceId, 0) b
                             WHERE msl.SettlementId = ${s1} AND b.Balance > 0 ORDER BY msl.InvoiceId`)
    const feeBefore = num(db, `SELECT LateFeeCharged FROM RentInvoices WHERE Id = ${owedInv}`)
    r = await a.post(`/Rent/ChargeLateFee?invoiceId=${owedInv}`)
    ok(r.status === 400 && /move-out settlement/.test(errorText(r)) && num(db, `SELECT LateFeeCharged FROM RentInvoices WHERE Id = ${owedInv}`) === feeBefore,
       'no late fee can be charged on a settled invoice', r.json)
    const unpaidDed = num(db, `SELECT ISNULL(MAX(ri.Id), 0) FROM RentInvoices ri JOIN MoveOutSettlementLines msl ON msl.InvoiceId = ri.Id
                               WHERE msl.SettlementId = ${s1} AND msl.LineType = 'Deduction'`)
    r = await a.patch('/RentHistory/CancelInvoice', { invoiceId: unpaidDed || owedInv, reason: 'e2e' })
    ok(r.status === 400 && (/move-out settlement/.test(errorText(r)) || /payment records/.test(errorText(r))) &&
       num(db, `SELECT StatusId FROM RentInvoices WHERE Id = ${unpaidDed || owedInv}`) !== 6, 'settled invoice cannot be cancelled', r.json)
    r = await a.put('/Rent/BulkUpdateDueDate', { invoiceIds: [owedInv], newDueDate: TODAY })
    ok(r.status === 400 && /move-out settlement/.test(errorText(r)), 'due date of a settled invoice cannot be moved', r.json)
    ok((await a.get('/RentHistory/History')).json.find((x) => x.invoiceId === cancelledCharge)?.canReinstate === false, 'History: reinstate not offered for a settled tenancy')
    r = await a.patch('/RentHistory/ReinstateInvoice', cancelledCharge)
    ok(r.status === 400 && /move-out settlement/.test(errorText(r)) && num(db, `SELECT StatusId FROM RentInvoices WHERE Id = ${cancelledCharge}`) === 6,
       'an invoice of a settled tenancy cannot be reinstated', r.json)
    const g = (await a.get(`/Settlements/Get?id=${s1}`)).json
    ok(g && Object.keys(g).length > 0, 'settlement statement data available')
  }

  // ---- tenancy 2: deposit larger than what is owed -> refund
  section('settlement: deposit larger than the dues, refunds')
  {
    const t2 = await apiTenancy(a, db, { start: monthStart(TODAY), rent: 40000 })
    await a.post('/Rent/GenerateInvoices', { tenantIds: [t2.tenantId], ...parts(TODAY) })
    let r = await record(t2, 60000)
    ok(r.status === 200, 'deposit received (60,000, no agreed amount)', r.json)
    await a.post('/Lease/Terminate', { leaseId: t2.leaseId, reason: 'E2E move-out', moveOutDate: TODAY })
    const outstanding = openBalance(t2)
    const refundDue = round2(60000 - outstanding)
    const body = { tenantId: t2.tenantId, unitId: t2.unitId, leaseId: t2.leaseId, expectedOutstanding: outstanding, expectedCredit: 0, expectedHeld: 60000, settlementDate: TODAY, acknowledgeUnbilled: true }
    r = await a.post('/Settlements/Finalize', { ...body, refund: { amount: refundDue + 1, paymentMethod: 'Cash' } })
    ok(r.status === 400 && /refundable amount/.test(errorText(r)), 'refund above the refundable amount refused', r.json)
    r = await a.post('/Settlements/Finalize', { ...body, refund: { amount: 5000, paymentMethod: 'Bank Transfer', reference: 'E2E' } })
    const s2 = r.json?.id
    ok(r.status === 200 && s2 > 0, 'settled with a part refund now', r.json)
    ok(openBalance(t2) === 0 && balanceOf(db, num(db, `SELECT MAX(Id) FROM RentInvoices WHERE TenantId = ${t2.tenantId}`)).status === 1, 'all dues paid from the deposit')
    ok(sql(db, `SELECT RefundDue, RefundPaid FROM MoveOutSettlements WHERE Id = ${s2}`)[0].map(Number).join('|') === `${refundDue}|5000`, `refund due ${refundDue}, 5,000 paid`)
    ok(held(t2) === round2(refundDue - 5000), 'deposit still held = refund still owed')
    r = await a.post('/Settlements/RecordRefund', { settlementId: s2, amount: refundDue, refundDate: TODAY, paymentMethod: 'Cash' })
    ok(r.status === 400 && /can't be more than/.test(errorText(r)), 'later refund above what is left refused', r.json)
    r = await a.post('/Settlements/RecordRefund', { settlementId: s2, amount: round2(refundDue - 5000), refundDate: TODAY, paymentMethod: 'Cash', reference: 'E2E' })
    ok(r.status === 200 && held(t2) === 0, 'remaining refund recorded, nothing held', r.json)
    const list = (await a.get('/Settlements/GetAll')).json
    const mine = list?.settlements?.find((x) => x.id === s2 || x.settlementId === s2)
    ok(mine && Number(mine.refundDue) - Number(mine.refundPaid) === 0, 'settlement closed in the list', mine)
    ok(num(db, `SELECT COUNT(*) FROM SecurityDeposits WHERE TenantId = ${t2.tenantId} AND EntryType = N'Refund'`) === 2, 'two refund entries in the deposit ledger')

    // Tenant delete: only once nothing is left to settle
    const del = (t) => a.del(`/Tenant/Delete?tenantId=${t.tenantId}`)
    const deleted = (t) => num(db, `SELECT IsDeleted FROM Tenants WHERE TenantId = ${t.tenantId}`) === 1
    r = await del(t1)
    ok(r.status === 400 && /owes money or has a credit/.test(errorText(r)) && !deleted(t1), 'tenant who still owes cannot be deleted', r.json)
    const t3 = await apiTenancy(a, db, { start: monthStart(TODAY), rent: 20000 })
    r = await del(t3)
    ok(r.status === 400 && /active lease/.test(errorText(r)) && !deleted(t3), 'tenant with an active lease cannot be deleted', r.json)
    await record(t3, 10000)
    await a.post('/Lease/Terminate', { leaseId: t3.leaseId, reason: 'E2E', moveOutDate: TODAY })
    r = await del(t3)
    ok(r.status === 400 && /deposit is still held/.test(errorText(r)) && !deleted(t3), 'tenant with a deposit still held cannot be deleted', r.json)
    r = await del(t2)
    ok(r.status === 200 && deleted(t2), 'fully settled tenant can be deleted', r.json)
    ok(num(db, `SELECT COUNT(*) FROM RentInvoices WHERE TenantId = ${t2.tenantId}`) > 0 && num(db, `SELECT COUNT(*) FROM SecurityDeposits WHERE TenantId = ${t2.tenantId}`) > 0,
       'its invoices and deposit history are kept')
    r = await del(t2)
    ok(r.status === 400 && /not found or already deleted/.test(errorText(r)), 'deleting twice refused', r.json)
  }

  // ---- tenancy 4: settled with unbilled months skipped -> those months are never billed afterwards
  section('settled tenancy: skipped months are not billed later')
  {
    const t4 = await apiTenancy(a, db, { start: monthStart(TODAY, -2), rent: 30000 })
    await a.post('/Rent/GenerateInvoices', { tenantIds: [t4.tenantId], ...parts(TODAY) }) // only this month billed
    await a.post('/Lease/Terminate', { leaseId: t4.leaseId, reason: 'E2E move-out', moveOutDate: TODAY })
    const pv = (await a.get(`/Settlements/Preview?tenantId=${t4.tenantId}&unitId=${t4.unitId}`)).json
    ok((pv?.unbilled || []).length === 2, 'preview lists the 2 unbilled months', pv?.unbilled)
    let r = await a.post('/Settlements/Finalize', {
      tenantId: t4.tenantId, unitId: t4.unitId, leaseId: t4.leaseId, expectedOutstanding: openBalance(t4), expectedCredit: 0, expectedHeld: 0,
      settlementDate: TODAY, acknowledgeUnbilled: true,
    })
    ok(r.status === 200 && r.json?.id > 0, 'settled, unbilled months skipped', r.json)
    const invoices = () => num(db, `SELECT COUNT(*) FROM RentInvoices WHERE TenantId = ${t4.tenantId} AND StatusId <> 6`)
    const before = invoices()
    await a.post('/Rent/GenerateInvoices', { tenantIds: [t4.tenantId], ...parts(monthStart(TODAY, -2)) })
    await a.post('/Rent/GenerateInvoices', { ...parts(monthStart(TODAY, -1)) }) // all tenants
    ok(invoices() === before, 'a skipped month of a settled tenancy is not billed afterwards (selected or all tenants)', { before, after: invoices() })
    // The same tenant renting the same unit again later is a new tenancy and is billed normally
    r = await a.post('/Lease/Create', { tenantId: t4.tenantId, unitId: t4.unitId, rentAmount: 30000, startDate: monthStart(TODAY, 1), tenureMonths: 12 })
    ok(r.status === 200, 'new lease for the same tenant and unit', r.json)
    await a.post('/Rent/GenerateInvoices', { tenantIds: [t4.tenantId], ...parts(monthStart(TODAY, 1)) })
    ok(invoices() === before + 1, 'the new tenancy is billed', { before, after: invoices() })
  }
}
