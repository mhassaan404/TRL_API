// Rent generation and the invoice lifecycle: generate, re-run safely, extra charges, late fee charge / reverse,
// due-date change, cancel / reinstate, late-fee settings snapshot
import { DB, TODAY, addDays, apiTenancy, area, balanceOf, errorText, expectedLateFee, monthStart, num, ok, one, section, sql } from '../lib.mjs'

const parts = (d) => ({ month: Number(d.slice(5, 7)), year: Number(d.slice(0, 4)) })

export default async ({ a }) => {
  area('invoices')
  const db = DB.a
  const t = await apiTenancy(a, db, { start: monthStart(TODAY, -1), rent: 40000, tenure: 12 })
  const other = await apiTenancy(a, db, {}) // a tenant without a lease
  const gen = (d, tenantIds = [t.tenantId], extra = {}) => a.post('/Rent/GenerateInvoices', { tenantIds, ...parts(d), ...extra })
  const invoiceFor = (d) => num(db, `SELECT ISNULL(MAX(Id), 0) FROM RentInvoices WHERE LeaseId = ${t.leaseId} AND InvoiceMonth = '${d}' AND StatusId <> 6`)
  const settings = (await a.get('/LateFeeSettings/Get')).json

  section('generate rent')
  let r = await gen(monthStart(TODAY, -13))
  ok(r.status === 400 && /can only be generated for/.test(errorText(r)), 'more than 12 months back refused', r.json)
  r = await gen(monthStart(TODAY, 2))
  ok(r.status === 400 && /can only be generated for/.test(errorText(r)), 'more than next month ahead refused', r.json)
  r = await gen(monthStart(TODAY, -12)) // inside the window; refused only because this lease starts later
  ok(r.status === 400 && /No lease covers/.test(errorText(r)), '12 months back is inside the billing window', r.json)
  r = await gen(TODAY, [t.tenantId], { dueInDays: 91 })
  ok(r.status === 400 && /between 0 and 90/.test(errorText(r)), 'due days above 90 refused', r.json)
  r = await gen(TODAY, [other.tenantId])
  ok(r.status === 400 && /No lease covers/.test(errorText(r)) && errorText(r).includes(other.name), 'tenant without a lease: named in the error', r.json)
  r = await gen(TODAY)
  const cur = invoiceFor(monthStart(TODAY))
  ok(r.status === 200 && r.json?.rowsAffected === 1 && cur > 0, 'this month generated', r.json)
  const [row] = sql(db, `SELECT TotalRent, CONVERT(varchar(10), InvoiceDate, 23), CONVERT(varchar(10), DueDate, 23), LateFeePerDay, LateFeeMaxMultiplier, StatusId, UnitId FROM RentInvoices WHERE Id = ${cur}`)
  ok(Number(row[0]) === 40000 && row[1] === monthStart(TODAY) && row[2] === addDays(monthStart(TODAY), settings.paymentDueDays) &&
     Number(row[3]) === settings.lateFeePerDay && Number(row[4]) === settings.maxLateFeeMultiplier && row[5] === '2' && Number(row[6]) === t.unitId,
     'full month rent, due date = invoice date + due days, late-fee rule snapshotted, Unpaid, on the lease unit', row)
  r = await gen(TODAY)
  ok(r.status === 200 && r.json?.rowsAffected === 0 && /already invoiced/.test(r.json?.message), 're-running is safe: nothing generated twice', r.json)
  r = await gen(TODAY, [t.tenantId, other.tenantId])
  ok(r.status === 200 && /No lease for/.test(r.json?.message), 'mixed selection: already-invoiced reported, tenant without a lease named', r.json)
  ok(num(db, `SELECT COUNT(*) FROM RentInvoices WHERE LeaseId = ${t.leaseId} AND InvoiceMonth = '${monthStart(TODAY)}' AND StatusId <> 6`) === 1, 'exactly one invoice for the month')
  r = await gen(monthStart(TODAY, -1))
  const prev = invoiceFor(monthStart(TODAY, -1))
  ok(r.status === 200 && prev > 0, 'last month generated', r.json)
  const coll = (await a.get('/Rent/GetRentCollection')).json
  ok(coll.some((x) => x.invoiceId === cur) && coll.some((x) => x.invoiceId === prev), 'both invoices in Rent Collection')

  section('late fee: charge and reverse')
  {
    const fee = expectedLateFee(db, prev)
    const b0 = balanceOf(db, prev)
    if (b0.openFee === 0) {
      ok(true, 'last month not overdue yet today: late-fee checks skipped')
    } else {
      ok(b0.openFee === fee, `open late fee = rule (${fee})`, b0)
      r = await a.post(`/Rent/ChargeLateFee?invoiceId=${prev}`)
      ok(r.status === 200 && num(db, `SELECT LateFeeCharged FROM RentInvoices WHERE Id = ${prev}`) === fee, 'late fee charged onto the invoice', r.json)
      const b1 = balanceOf(db, prev)
      ok(b1.balance === 40000 + fee && b1.openFee === 0, 'balance includes the charged fee, nothing more accrues', b1)
      r = await a.post(`/Rent/ChargeLateFee?invoiceId=${prev}`)
      ok(r.status === 400 && /already charged/.test(errorText(r)), 'charging twice refused', r.json)
      r = await a.post('/Rent/ReverseLateFee', { invoiceId: prev, reason: ' ' })
      ok(r.status === 400 && /Reason is required/.test(errorText(r)), 'reverse late fee needs a reason', r.json)
      r = await a.post('/Rent/ReverseLateFee', { invoiceId: prev, reason: 'x'.repeat(301) })
      ok(r.status === 400 && /at most 300/.test(errorText(r)) && num(db, `SELECT LateFeeCharged FROM RentInvoices WHERE Id = ${prev}`) === fee,
         'reverse late fee reason longer than 300 refused (was a server error)', r.json)
      r = await a.post('/Rent/ReverseLateFee', { invoiceId: prev, reason: 'E2E goodwill' })
      ok(r.status === 200 && num(db, `SELECT LateFeeCharged FROM RentInvoices WHERE Id = ${prev}`) === 0, 'late fee reversed', r.json)
      ok(one(db, `SELECT CONCAT(Action, '|', Amount, '|', Reason) FROM InvoiceAudit WHERE InvoiceId = ${prev} AND Action = 'LATE_FEE_REVERSED'`) === `LATE_FEE_REVERSED|${fee.toFixed(2)}|E2E goodwill`,
         'reversal written to the invoice audit')
      const res = await Promise.all(Array.from({ length: 6 }, () => a.post(`/Rent/ChargeLateFee?invoiceId=${prev}`)))
      const b2 = balanceOf(db, prev)
      ok(res.filter((x) => x.status === 200).length === 1 && num(db, `SELECT LateFeeCharged FROM RentInvoices WHERE Id = ${prev}`) === fee &&
         b2.pending === b2.balance && b2.balance === 40000 + fee, '6 parallel charges: charged exactly once, invoice totals consistent', { codes: res.map((x) => x.status), b2 })
      const [rv1, rv2] = await Promise.all([1, 2].map(() => a.post('/Rent/ReverseLateFee', { invoiceId: prev, reason: 'E2E race' })))
      ok([rv1.status, rv2.status].sort().join() === '200,400' && num(db, `SELECT COUNT(*) FROM InvoiceAudit WHERE InvoiceId = ${prev} AND Action = 'LATE_FEE_REVERSED'`) === 2,
         '2 parallel reversals: reversed once, one audit row', [rv1.status, rv2.status])
    }
  }

  section('due date change')
  {
    const newDue = addDays(TODAY, 15)
    r = await a.put('/Rent/BulkUpdateDueDate', { invoiceIds: [cur], newDueDate: newDue })
    ok(r.status === 200 && one(db, `SELECT CONVERT(varchar(10), DueDate, 23) FROM RentInvoices WHERE Id = ${cur}`) === newDue, 'due date updated', r.json)
    r = await a.post(`/Rent/ChargeLateFee?invoiceId=${cur}`)
    ok(r.status === 400 && num(db, `SELECT LateFeeCharged FROM RentInvoices WHERE Id = ${cur}`) === 0, 'no late fee on an invoice that is not overdue', r.json)
    ok(one(db, `SELECT TOP 1 CONCAT(Action, '|', IIF(CreatedBy IS NULL, 'no user', 'user')) FROM InvoiceAudit WHERE InvoiceId = ${cur} ORDER BY Id DESC`) === 'DUE_DATE_CHANGED|user',
       'due date change written to the invoice audit with the user')
    r = await a.put('/Rent/BulkUpdateDueDate', { invoiceIds: [], newDueDate: newDue })
    ok(r.status === 400 && /at least one invoice/.test(errorText(r)), 'empty selection refused', r.json)
    r = await a.put('/Rent/BulkUpdateDueDate', { invoiceIds: [cur], newDueDate: '0001-01-01' })
    ok(r.status === 400 && /no more than a year/.test(errorText(r)), 'impossible date refused (was a server error)', r.json)
    r = await a.put('/Rent/BulkUpdateDueDate', { invoiceIds: [cur], newDueDate: addDays(monthStart(TODAY), -1) })
    ok(r.status === 400 && one(db, `SELECT CONVERT(varchar(10), DueDate, 23) FROM RentInvoices WHERE Id = ${cur}`) === newDue, 'date before the invoice date refused, nothing changed', r.json)
    // Pay last month in full, then try to move its due date together with this month's
    await a.post('/Rent/SubmitPayments', [{ tenantId: t.tenantId, rentInvoiceId: prev, paymentAmount: num(db, `SELECT b.Balance FROM dbo.InvoiceBalance(${prev}, 0) b`), paymentDate: TODAY, paymentMethod: 'Cash', discountAmount: 0, discountPercent: 0, isLateFeeWaived: true }])
    const prevDue = one(db, `SELECT CONVERT(varchar(10), DueDate, 23) FROM RentInvoices WHERE Id = ${prev}`)
    const due2 = addDays(TODAY, 16)
    r = await a.put('/Rent/BulkUpdateDueDate', { invoiceIds: [cur, prev], newDueDate: due2 })
    ok(r.status === 200 && /1 skipped/.test(r.json?.message) && one(db, `SELECT CONVERT(varchar(10), DueDate, 23) FROM RentInvoices WHERE Id = ${cur}`) === due2 &&
       one(db, `SELECT CONVERT(varchar(10), DueDate, 23) FROM RentInvoices WHERE Id = ${prev}`) === prevDue, 'paid invoice skipped, open one updated', r.json)
    ok(num(db, `SELECT COUNT(*) FROM InvoiceAudit WHERE InvoiceId = ${prev} AND Action = 'DUE_DATE_CHANGED'`) === 0, 'no audit row for the skipped invoice')
  }

  section('extra charges')
  {
    const base = { tenantIds: [t.tenantId], chargeType: 'Damage', description: 'E2E broken window', amount: 2500, relatedInvoiceId: cur }
    r = await a.post('/Rent/CreateExtraCharge', { ...base, amount: 0 })
    ok(r.status === 400 && /Amount must be greater than 0/.test(errorText(r)), 'zero amount refused', r.json)
    r = await a.post('/Rent/CreateExtraCharge', { ...base, chargeType: 'Party' })
    ok(r.status === 400 && /Charge type must be one of/.test(errorText(r)), 'unknown charge type refused', r.json)
    r = await a.post('/Rent/CreateExtraCharge', { ...base, relatedInvoiceId: invoiceFor(monthStart(TODAY)) + 1000000 })
    ok(r.status === 400 && /related invoice/.test(errorText(r)), 'related invoice that does not exist refused', r.json)
    r = await a.post('/Rent/CreateExtraCharge', { ...base, tenantIds: [t.tenantId, other.tenantId] })
    ok(r.status === 400 && /one tenant/.test(errorText(r)), 'related invoice with several tenants refused', r.json)
    r = await a.post('/Rent/CreateExtraCharge', base)
    const xc = num(db, `SELECT MAX(Id) FROM RentInvoices WHERE TenantId = ${t.tenantId} AND ChargeType = 'Damage'`)
    ok(r.status === 200 && xc > 0, 'extra charge created as its own invoice', r.json)
    ok(one(db, `SELECT CONCAT(TotalRent, '|', ISNULL(LeaseId, 0), '|', RelatedInvoiceId, '|', UnitId, '|', StatusId) FROM RentInvoices WHERE Id = ${xc}`) === `2500.00|0|${cur}|${t.unitId}|2`,
       'not linked to the lease, linked to the related invoice, on its unit, Unpaid')
    ok(num(db, `SELECT TotalRent FROM RentInvoices WHERE Id = ${cur}`) === 40000, 'related rent invoice unchanged')
    r = await a.post('/Rent/CreateExtraCharge', base)
    ok(r.status === 400 && /just added/.test(errorText(r)), 'same charge again within a minute refused', r.json)
    r = await a.post('/Rent/CreateExtraCharge', { ...base, amount: 100, applyLateFee: false, relatedInvoiceId: null })
    const noFee = num(db, `SELECT MAX(Id) FROM RentInvoices WHERE TenantId = ${t.tenantId} AND ChargeType = 'Damage'`)
    ok(r.status === 200 && num(db, `SELECT LateFeePerDay FROM RentInvoices WHERE Id = ${noFee}`) === 0, '"no late fee" charge stores a zero rate', r.json)
    const det = (await a.get(`/RentHistory/InvoiceDetails?invoiceId=${cur}`)).json
    ok(det.linkedCharges?.some((c) => c.invoiceId === xc), 'rent invoice History lists the linked charge', det.linkedCharges)
  }

  section('cancel and reinstate')
  {
    r = await a.patch('/RentHistory/CancelInvoice', { invoiceId: cur, reason: 'x'.repeat(301) })
    ok(r.status === 400 && /at most 300/.test(errorText(r)) && num(db, `SELECT StatusId FROM RentInvoices WHERE Id = ${cur}`) !== 6,
       'cancel reason longer than 300 refused (was a server error)', r.json)
    r = await a.patch('/RentHistory/CancelInvoice', { invoiceId: cur, reason: '  ' })
    ok(r.status === 400 && /Reason is required/.test(errorText(r)), 'cancel needs a reason', r.json)
    r = await a.patch('/RentHistory/CancelInvoice', { invoiceId: cur, reason: 'e2e' })
    ok(r.status === 200 && num(db, `SELECT StatusId FROM RentInvoices WHERE Id = ${cur}`) === 6, 'unpaid invoice cancelled', r.json)
    ok(!((await a.get('/Rent/GetRentCollection')).json || []).some((x) => x.invoiceId === cur), 'cancelled invoice not in Rent Collection')
    ok(one(db, `SELECT COUNT(*) FROM InvoiceAudit WHERE InvoiceId = ${cur} AND Action = 'CANCELLED'`) === '1', 'cancel written to the invoice audit')
    r = await a.patch('/RentHistory/CancelInvoice', { invoiceId: cur, reason: 'e2e' })
    ok(r.status === 400, 'cancelling twice refused', r.json)
    r = await gen(TODAY)
    const again = invoiceFor(monthStart(TODAY))
    ok(r.status === 200 && again > cur, 'a cancelled month can be generated again', r.json)
    let h = (await a.get('/RentHistory/History')).json.find((x) => x.invoiceId === cur)
    ok(h?.canReinstate === false, 'History: reinstate not offered while another invoice bills the month', h)
    r = await a.patch('/RentHistory/ReinstateInvoice', cur)
    ok(r.status !== 200 && num(db, `SELECT StatusId FROM RentInvoices WHERE Id = ${cur}`) === 6, 'reinstate refused (would bill the month twice)', r.json)
    await a.patch('/RentHistory/CancelInvoice', { invoiceId: again, reason: 'e2e' })
    h = (await a.get('/RentHistory/History')).json.find((x) => x.invoiceId === cur)
    ok(h?.canReinstate === true, 'History: reinstate offered once the month is free again')
    r = await a.post('/Rent/CreateExtraCharge', { tenantIds: [t.tenantId], chargeType: 'Rent Correction', description: 'E2E on cancelled', amount: 500, relatedInvoiceId: again })
    ok(r.status === 400 && /is cancelled/.test(errorText(r)), 'extra charge cannot point to a cancelled invoice', r.json)
    r = await a.patch('/RentHistory/ReinstateInvoice', cur)
    ok(r.status === 200 && num(db, `SELECT StatusId FROM RentInvoices WHERE Id = ${cur}`) === 2, 'reinstated as Unpaid', r.json)
    ok(one(db, `SELECT CONCAT(COUNT(*), '|', MIN(IIF(CreatedBy IS NULL, 0, 1))) FROM InvoiceAudit WHERE InvoiceId = ${cur} AND Action = 'REINSTATED'`) === '1|1', 'reinstate written to the invoice audit with the user')
    r = await a.patch('/RentHistory/ReinstateInvoice', cur)
    ok(r.status === 400 && /not cancelled/.test(errorText(r)), 'reinstating an invoice that is not cancelled is refused', r.json)
  }

  section('late-fee settings apply to new invoices only')
  {
    r = await a.post('/LateFeeSettings/Save', { paymentDueDays: 100, lateFeePerDay: 300, maxLateFeeMultiplier: 1 })
    ok(r.status === 400, 'invalid settings refused', r.json)
    r = await a.post('/LateFeeSettings/Save', { paymentDueDays: 7, lateFeePerDay: 300, maxLateFeeMultiplier: 1 })
    ok(r.status === 200, 'settings saved', r.json)
    r = await gen(monthStart(TODAY, 1))
    const next = invoiceFor(monthStart(TODAY, 1))
    ok(r.status === 200 && next > 0, 'next month generated', r.json)
    ok(one(db, `SELECT CONCAT(LateFeePerDay, '|', LateFeeMaxMultiplier, '|', DATEDIFF(DAY, InvoiceDate, DueDate)) FROM RentInvoices WHERE Id = ${next}`) === '300.00|1.00|7',
       'new invoice uses the new rate, cap and due days')
    ok(one(db, `SELECT CONCAT(LateFeePerDay, '|', LateFeeMaxMultiplier) FROM RentInvoices WHERE Id = ${cur}`) === `${settings.lateFeePerDay.toFixed(2)}|${settings.maxLateFeeMultiplier.toFixed(2)}`,
       'existing invoice keeps its original rule')
    r = await a.post('/LateFeeSettings/Save', { paymentDueDays: settings.paymentDueDays, lateFeePerDay: settings.lateFeePerDay, maxLateFeeMultiplier: settings.maxLateFeeMultiplier })
    ok(r.status === 200, 'settings restored', r.json)
  }
}
