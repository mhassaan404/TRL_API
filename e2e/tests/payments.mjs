// Payments: recording rules, discounts, waivers, adjustments and payment reversal (the 2026-10-09 reversal suite,
// moved here from a session scratch script).
import {
  DB, TODAY, addDays, area, balanceOf, errorText, expectedLateFee, monthStart, num, ok, one, paymentRow, section, seed, sql,
} from '../lib.mjs'

export default async ({ a, b }) => {
  area('payments')
  const db = DB.a
  // Invoices months back so they are overdue today, and two not due yet
  const due = (months, day = 5) => addDays(monthStart(TODAY, months), day - 1)
  const s = seed(db, {
    units: 2,
    tenants: [{ name: 'E2E Pay Tenant A', unit: 0 }, { name: 'E2E Pay Tenant B', unit: 1 }],
    leases: [
      { tenant: 0, unit: 0, start: monthStart(TODAY, -8), tenure: 24, billedThrough: addDays(monthStart(TODAY, 3), -1) },
      { tenant: 1, unit: 1, start: monthStart(TODAY), tenure: 12, billedThrough: addDays(monthStart(TODAY, 1), -1) },
    ],
    invoices: [
      { tag: 'I1', month: monthStart(TODAY, -2), due: due(-2) },
      { tag: 'I2', month: monthStart(TODAY, -1), due: due(-1) },
      { tag: 'I3', month: monthStart(TODAY), due: addDays(TODAY, 20) },
      { tag: 'I4', month: monthStart(TODAY, -3), due: due(-3) },
      { tag: 'I5', month: monthStart(TODAY, -4), due: due(-4) },
      { tag: 'I6', month: monthStart(TODAY, -5), due: due(-5) },
      { tag: 'I9', month: monthStart(TODAY, -6), due: addDays(TODAY, 40) },
      { tag: 'I10', month: monthStart(TODAY, -7), due: addDays(TODAY, 40) },
      { tag: 'IB', tenant: 1, unit: 1, lease: 1, month: monthStart(TODAY), due: addDays(TODAY, 20) },
      { tag: 'I8', month: monthStart(TODAY), due: addDays(TODAY, 20), amount: 3000, chargeType: 'Utility', lateFeePerDay: 0 },
    ],
  })
  const ids = { ...s.inv, TA: s.tenants[0], TB: s.tenants[1], LA: s.leases[0], LB: s.leases[1], U1: s.units[0] }

  const pay = async (inv, { cash = 0, disc = 0, waive = false, method = 'Cash', tenant = ids.TA, date = TODAY } = {}) => {
    const r = await a.post('/Rent/SubmitPayments', [{
      tenantId: tenant, rentInvoiceId: inv, paymentAmount: cash, paymentDate: date, paymentMethod: method,
      notes: 'e2e', discountAmount: disc, discountPercent: 0, isLateFeeWaived: waive,
    }])
    if (r.status !== 200) throw new Error(`pay ${inv}: ${r.status} ${JSON.stringify(r.json)}`)
    return num(db, `SELECT MAX(Id) FROM Payments WHERE RentInvoiceId = ${inv}`)
  }
  const reverse = (id, reason = 'Entered by mistake (e2e)', session = a, opts) => session.post('/Rent/ReversePayment', { paymentId: id, reason }, opts)
  const bal = (inv) => balanceOf(db, inv)
  const reversalOf = (id) => sql(db, `SELECT Id, PaymentAmount, DiscountAmount, IsLateFeeWaived, DiscountPercent, PaymentMethod, Notes, CreatedBy, TenantId, RentInvoiceId
                                      FROM Payments WHERE ReversalOfPaymentId = ${id}`)
  const inCollection = async (inv) => ((await a.get('/Rent/GetRentCollection')).json || []).some((r) => r.invoiceId === inv)
  const details = async (inv) => (await a.get(`/RentHistory/InvoiceDetails?invoiceId=${inv}`)).json
  const originals = {}
  const remember = (id) => { originals[id] = paymentRow(db, id) }

  section('recording rules')
  {
    let r = await a.post('/Rent/SubmitPayments', [{ tenantId: ids.TA, rentInvoiceId: ids.I10, paymentAmount: 60000, paymentDate: TODAY, paymentMethod: 'Cash', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false }])
    ok(r.status === 400 && /exceeds the balance/.test(errorText(r)), 'more than the balance is refused', r.json)
    r = await a.post('/Rent/SubmitPayments', [{ tenantId: ids.TB, rentInvoiceId: ids.I10, paymentAmount: 100, paymentDate: TODAY, paymentMethod: 'Cash', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false }])
    ok(r.status === 400 && /does not belong/.test(errorText(r)), 'invoice of another tenant is refused', r.json)
    r = await a.post('/Rent/SubmitPayments', [{ tenantId: ids.TA, rentInvoiceId: ids.I10, paymentAmount: -5, paymentDate: TODAY, paymentMethod: 'Cash', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false }])
    ok(r.status === 400, 'negative amount is refused', r.status)
    r = await a.post('/Rent/SubmitPayments', [{ tenantId: ids.TA, rentInvoiceId: ids.I10, paymentAmount: 100, paymentDate: TODAY, paymentMethod: '', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false }])
    ok(r.status === 400, 'cash without a method is refused', r.status)
    const row = (over) => [{ tenantId: ids.TA, rentInvoiceId: ids.I10, paymentAmount: 100, paymentDate: TODAY, paymentMethod: 'Cash', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false, ...over }]
    const countI10 = () => num(db, `SELECT COUNT(*) FROM Payments WHERE RentInvoiceId = ${ids.I10}`)
    const n0 = countI10()
    for (const [over, name, text] of [
      [{ paymentMethod: 'Security Deposit' }, '"Security Deposit" method refused for a normal payment', /must be one of/],
      [{ paymentMethod: 'Gold' }, 'unknown method refused', /must be one of/],
      [{ paymentDate: addDays(TODAY, 3) }, 'future payment date refused', /future/],
      [{ discountPercent: 150 }, 'discount % above 100 refused', /between 0 and 100/],
      [{ notes: 'x'.repeat(501) }, 'notes longer than 500 refused (was a server error)', /at most 500/],
      [{ paymentAmount: 10.005 }, 'more than 2 decimals refused', /2 decimal/],
    ]) {
      r = await a.post('/Rent/SubmitPayments', row(over))
      ok(r.status === 400 && text.test(errorText(r)), name, r.json)
    }
    r = await a.post('/Rent/SubmitPayments', [...row({ paymentAmount: 100 }), ...row({ paymentAmount: 200 })])
    ok(r.status === 400 && /appears more than once/.test(errorText(r)), 'same invoice twice in one submission refused with a clear message', r.json)
    ok(countI10() === n0, 'refused payments wrote nothing')
    const before = num(db, `SELECT COUNT(*) FROM Payments WHERE RentInvoiceId = ${ids.I10}`)
    r = await a.post('/Rent/SubmitPayments', [
      { tenantId: ids.TA, rentInvoiceId: ids.I10, paymentAmount: 100, paymentDate: TODAY, paymentMethod: 'Cash', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false },
      { tenantId: ids.TA, rentInvoiceId: ids.I9, paymentAmount: 999999, paymentDate: TODAY, paymentMethod: 'Cash', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false },
    ])
    ok(r.status === 400 && num(db, `SELECT COUNT(*) FROM Payments WHERE RentInvoiceId = ${ids.I10}`) === before, 'batch is all or nothing (first payment rolled back)', r.json)
    r = await a.post('/Rent/SubmitPayments', [{ tenantId: ids.TA, rentInvoiceId: ids.I10, paymentAmount: 1234, paymentDate: TODAY, paymentMethod: 'Cash', notes: 'dup', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false }])
    const pid = num(db, `SELECT MAX(Id) FROM Payments WHERE RentInvoiceId = ${ids.I10}`)
    ok(r.status === 200 && r.json?.receiptIds?.[0] === pid, 'payment recorded, its id returned as the receipt', r.json)
    const dup = await a.post('/Rent/SubmitPayments', [{ tenantId: ids.TA, rentInvoiceId: ids.I10, paymentAmount: 1234, paymentDate: TODAY, paymentMethod: 'Cash', notes: 'dup', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false }])
    ok(dup.status === 400 && /just recorded/.test(errorText(dup)), 'identical payment within 10 seconds is refused', dup.json)
    await reverse(pid, 'cleanup of the duplicate test')
  }

  section('security: auth, CSRF, removed edit route')
  {
    const p = await pay(ids.I10, { cash: 1000 }); remember(p)
    let r = await reverse(p, 'x', a, { anonymous: true })
    ok(r.status === 401, 'not logged in -> 401', r.status)
    r = await reverse(p, 'x', a, { xrw: false })
    ok(r.status === 403, 'missing X-Requested-With -> 403', r.status)
    ok(reversalOf(p).length === 0, 'blocked requests created no reversal row')
    r = await a.put('/Rent/UpdatePayments', [{ id: p, tenantId: ids.TA, rentInvoiceId: ids.I10, paymentAmount: 1, paymentDate: TODAY, paymentMethod: 'Cash' }])
    ok(r.status === 404 || r.status === 405, 'PUT /Rent/UpdatePayments no longer exists', r.status)
    ok(paymentRow(db, p) === originals[p], 'payment untouched by the removed edit route')
  }

  section('reversal validation')
  {
    const p = num(db, `SELECT MAX(Id) FROM Payments WHERE RentInvoiceId = ${ids.I10} AND ReversalOfPaymentId IS NULL`)
    for (const [reason, name] of [['', 'empty reason'], ['   ', 'blank reason'], ['x'.repeat(501), '501-char reason'], ['bad\u0001char', 'control char']]) {
      const r = await reverse(p, reason)
      ok(r.status === 400 && /reason|invalid/i.test(errorText(r)), `${name} -> 400`, r.json)
    }
    let r = await a.post('/Rent/ReversePayment', { paymentId: p })
    ok(r.status === 400, 'missing reason -> 400', r.status)
    r = await reverse(0)
    ok(r.status === 400 && /Payment is required/.test(errorText(r)), 'paymentId 0 -> 400', r.json)
    r = await reverse(99999999)
    ok(r.status === 400 && /not found/i.test(errorText(r)), 'unknown payment -> 400 not found', r.json)
    r = await a.call('POST', '/Rent/ReversePayment', 'garbage{', { raw: true })
    ok(r.status === 400, 'malformed body -> 400', r.status)
    ok(reversalOf(p).length === 0, 'invalid requests created no reversal row')
    r = await reverse(p, 'x'.repeat(500))
    ok(r.status === 200, '500-char reason accepted', r.json)
  }

  section('partial cash payment (not overdue)')
  {
    const p = await pay(ids.I3, { cash: 20000 }); remember(p)
    let x = bal(ids.I3)
    ok(x.balance === 30000 && x.status === 8, 'after payment: Partial 30,000', x)
    const r = await reverse(p, '  Wrong invoice  ')
    ok(r.status === 200 && r.json?.isSuccess && r.json?.id > p, 'reversal OK, returns new record id', r.json)
    ok(/Unpaid/.test(r.json?.message) && /50,000/.test(r.json?.message), 'message shows new status and amount due', r.json?.message)
    x = bal(ids.I3)
    ok(x.balance === 50000 && x.status === 2 && x.paid === 0 && x.pending === 50000, 'after reversal: Unpaid 50,000, cached pending updated', x)
    const rv = reversalOf(p)
    ok(rv.length === 1 && Number(rv[0][1]) === -20000 && Number(rv[0][2]) === 0 && rv[0][3] === '0' && rv[0][5] === 'Cash' &&
       rv[0][6] === 'Wrong invoice' && rv[0][7] !== 'NULL' && Number(rv[0][8]) === ids.TA && Number(rv[0][9]) === ids.I3,
       'reversal row: -20000, same method/tenant/invoice, trimmed reason, CreatedBy set', rv)
    ok(paymentRow(db, p) === originals[p], 'original row unchanged')
    ok(await inCollection(ids.I3), 'invoice back in Rent Collection')
    let again = await reverse(p)
    ok(again.status === 400 && /already been reversed/.test(errorText(again)), 'second reversal refused', again.json)
    again = await reverse(Number(rv[0][0]))
    ok(again.status === 400 && /itself a reversal/.test(errorText(again)), 'reversing the reversal refused', again.json)
    const p2 = await pay(ids.I3, { cash: 25000 })
    x = bal(ids.I3)
    ok(x.balance === 25000 && x.status === 8, 'corrected payment recorded normally afterwards', x)
    const det = await details(ids.I3)
    const o = det.payments.find((v) => v.paymentId === p)
    const v = det.payments.find((y) => y.reversalOfPaymentId === p)
    const n = det.payments.find((y) => y.paymentId === p2)
    ok(o?.reversedByPaymentId === v?.paymentId && o?.reversalReason === 'Wrong invoice' && o?.reversedBy && o?.reverseBlock === 'ALREADY_REVERSED',
       'History: original marked reversed (by, reason, block code)', o)
    ok(v?.reverseBlock === 'IS_REVERSAL' && n?.reverseBlock == null, 'History: reversal blocked, new payment reversible', { v: v?.reverseBlock, n: n?.reverseBlock })
    ok(det.payments.at(-1).balanceAfter === 25000, 'History running balance ends at 25,000', det.payments.map((y) => y.balanceAfter))
    const hist = (await a.get('/RentHistory/History')).json.find((y) => y.invoiceId === ids.I3)
    ok(hist?.balance === 25000 && hist?.paidAmount === 25000, 'Rent History list totals', hist)
    const modal = (await a.get(`/Rent/GetPaymentHistoryById?invoiceId=${ids.I3}`)).json || []
    ok(modal.find((y) => y.paymentId === p)?.reversedByPaymentId === v?.paymentId && modal.find((y) => y.paymentId === v?.paymentId)?.reversalOfPaymentId === p &&
       modal.find((y) => y.paymentId === p2)?.reversedByPaymentId == null, 'payment window history marks the reversed payment and its reversal', modal)
  }

  section('cash + discount + waiver together (overdue)')
  let fullPay
  {
    let r = await a.post('/Rent/SubmitPayments', [{ tenantId: ids.TA, rentInvoiceId: ids.I1, paymentAmount: 50000, paymentDate: TODAY, paymentMethod: 'Bank Transfer', notes: 'x', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false }])
    ok(r.status === 400 && /overdue/.test(errorText(r)), 'overdue invoice cannot be closed without charging or waiving the late fee', r.json)
    fullPay = await pay(ids.I1, { cash: 45000, disc: 5000, waive: true, method: 'Bank Transfer' }); remember(fullPay)
    let x = bal(ids.I1)
    ok(x.status === 1 && x.balance === 0 && x.waived === 1 && x.openFee === 0, 'paid in full with discount + waiver', x)
    ok(!(await inCollection(ids.I1)), 'paid invoice not in Rent Collection')
    ok((await details(ids.I1)).payments[0].reverseBlock == null, 'History offers Reverse')
    r = await reverse(fullPay, 'Wrong tenant')
    ok(r.status === 200, 'reversed', r.json)
    x = bal(ids.I1)
    const fee = expectedLateFee(db, ids.I1)
    ok(x.status === 2 && x.balance === 50000 && x.paid === 0 && x.disc === 0 && x.waived === 0, 'cash, discount and waiver all taken back', x)
    ok(x.openFee === fee && fee > 0, `late fee accrues again from the original due date (${fee})`, { open: x.openFee, fee })
    ok(await inCollection(ids.I1), 'back in Rent Collection')
    const rv = reversalOf(fullPay)[0]
    ok(Number(rv[1]) === -45000 && Number(rv[2]) === -5000 && rv[3] === '0' && Number(rv[4]) === 0 && rv[5] === 'Bank Transfer', 'reversal row mirrors cash/discount, no waiver flag', rv)
    ok(paymentRow(db, fullPay) === originals[fullPay], 'original unchanged')
    r = await a.get(`/Documents/Receipts?ids=${fullPay}`)
    ok(r.status === 200 && r.json[0].reversedByPaymentId === Number(rv[0]) && r.json[0].reversalReason === 'Wrong tenant' && r.json[0].reversedBy,
       'original receipt still printable, carries reversal info', r.json?.[0])
    r = await a.get(`/Documents/Receipts?ids=${rv[0]}`)
    ok(r.status !== 200 && /reversal of payment/.test(errorText(r)), 'no receipt for a reversal row', r.json)
    r = await a.get(`/Documents/Invoice?invoiceId=${ids.I1}`)
    const ps = r.json?.payments || []
    ok(ps.some((y) => y.paymentId === fullPay && y.reversedByPaymentId === Number(rv[0])) && ps.some((y) => y.reversalOfPaymentId === fullPay),
       'printable invoice lists original (reversed) and reversal', ps)
  }

  section('waiver-only and discount-only rows (overdue)')
  let waiverRow, discRow
  {
    const cash = await pay(ids.I2, { cash: 10000 }); remember(cash)
    waiverRow = await pay(ids.I2, { waive: true, method: 'Adjustment' }); remember(waiverRow)
    let x = bal(ids.I2)
    ok(x.waived === 1 && x.openFee === 0 && x.balance === 40000, 'waiver-only row stops the late fee', x)
    let r = await reverse(waiverRow, 'Waiver not approved')
    ok(r.status === 200, 'waiver-only row reversed', r.json)
    x = bal(ids.I2)
    ok(x.waived === 0 && x.openFee === expectedLateFee(db, ids.I2) && x.openFee > 0 && x.paid === 10000 && x.balance === 40000, 'late fee back; cash and balance unchanged', x)
    const wr = reversalOf(waiverRow)[0]
    ok(Number(wr[1]) === 0 && Number(wr[2]) === 0 && wr[3] === '0', 'waiver reversal row has no amounts', wr)
    discRow = await pay(ids.I2, { disc: 2000, method: 'Adjustment' }); remember(discRow)
    ok(bal(ids.I2).balance === 38000, 'discount-only row applied')
    r = await reverse(discRow, 'Discount not approved')
    x = bal(ids.I2)
    ok(r.status === 200 && x.disc === 0 && x.balance === 40000 && x.paid === 10000, 'discount-only row reversed', { r: r.json, x })
    ok(paymentRow(db, waiverRow) === originals[waiverRow] && paymentRow(db, discRow) === originals[discRow] && paymentRow(db, cash) === originals[cash], 'originals unchanged')
  }

  section('adjustments (legacy rows) and the NEGATIVE rule')
  {
    const p = await pay(ids.I4, { cash: 30000 }); remember(p)
    let r = await a.post('/Rent/CreatePaymentAdjustment', { tenantId: ids.TA, rentInvoiceId: ids.I4, paymentAmount: -10000, paymentMethod: 'Adjustment', notes: 'x', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false })
    ok(r.status === 404 && num(db, `SELECT COUNT(*) FROM Payments WHERE RentInvoiceId = ${ids.I4}`) === 1, 'Create Adjustment endpoint removed (404), nothing written', r.status)
    // An adjustment recorded before the endpoint was removed (same row shape the old endpoint wrote)
    const adj = num(db, `INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes, DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
      OUTPUT inserted.Id VALUES (${ids.TA}, -10000, GETDATE(), ${ids.I4}, N'Adjustment', N'Bounced part', 0, 0, 0, 1, GETDATE())`); remember(adj)
    r = await reverse(adj)
    ok(r.status === 400 && /Adjustments can't be reversed/.test(errorText(r)), 'adjustment cannot be reversed', r.json)
    r = await reverse(p)
    ok(r.status === 400 && /negative total/.test(errorText(r)), 'payment partly taken back by an adjustment cannot be reversed', r.json)
    ok(bal(ids.I4).paid === 20000 && reversalOf(p).length === 0 && reversalOf(adj).length === 0, 'nothing changed on the invoice')
    const det = await details(ids.I4)
    ok(det.payments.find((y) => y.paymentId === adj)?.reverseBlock === 'ADJUSTMENT' && det.payments.find((y) => y.paymentId === p)?.reverseBlock === 'NEGATIVE',
       'History shows the same block codes')
  }

  section('settlement protection and cancelled invoices')
  {
    const p = await pay(ids.I5, { cash: 10000 }); remember(p)
    const dep = num(db, `INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes, DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
      OUTPUT inserted.Id VALUES (${ids.TA}, 5000, GETDATE(), ${ids.I6}, N'Security Deposit', N'e2e', 0, 0, 0, 1, GETDATE())`)
    let r = await reverse(dep)
    ok(r.status === 400 && /settlement/.test(errorText(r)), "'Security Deposit' payment refused", r.json)
    const sid = num(db, `INSERT INTO MoveOutSettlements (TenantId, UnitId, LeaseId, SettlementDate, OutstandingBefore, CreditBefore, DeductionsTotal, DepositHeldBefore, DepositApplied, TenantOwes, RefundDue)
      OUTPUT inserted.Id VALUES (${ids.TA}, ${ids.U1}, ${ids.LA}, '${TODAY}', 40000, 0, 0, 0, 0, 40000, 0)`)
    sql(db, `INSERT INTO MoveOutSettlementLines (SettlementId, InvoiceId, LineType, BalanceBefore) VALUES (${sid}, ${ids.I5}, 'Outstanding', 40000)`)
    r = await reverse(p)
    ok(r.status === 400 && /settlement/.test(errorText(r)), 'payment on a settled invoice refused', r.json)
    const cashP = await pay(ids.I9, { cash: 1000 }); remember(cashP)
    sql(db, `INSERT INTO MoveOutSettlementLines (SettlementId, InvoiceId, LineType, BalanceBefore, CashReceived, CashPaymentId) VALUES (${sid}, ${ids.IB}, 'Outstanding', 50000, 1000, ${cashP})`)
    r = await reverse(cashP)
    ok(r.status === 400 && /settlement/.test(errorText(r)), 'final payment taken at settlement refused (CashPaymentId)', r.json)
    sql(db, `DELETE FROM MoveOutSettlementLines WHERE SettlementId = ${sid} AND InvoiceId = ${ids.IB}`)
    sql(db, `UPDATE RentInvoices SET StatusId = 6 WHERE Id = ${ids.I6}`)
    const c6 = num(db, `INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes, DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
      OUTPUT inserted.Id VALUES (${ids.TA}, 100, GETDATE(), ${ids.I6}, N'Cash', N'legacy', 0, 0, 0, 1, GETDATE())`)
    r = await reverse(c6)
    ok(r.status === 400 && /cancelled/.test(errorText(r)), 'payment on a cancelled invoice refused', r.json)
    ok(one(db, `SELECT COUNT(*) FROM Payments WHERE ReversalOfPaymentId IN (${dep}, ${p}, ${cashP}, ${c6})`) === '0', 'no reversal rows written for refused cases')
  }

  section('cancel invoice after full reversal')
  {
    const p = await pay(ids.I8, { cash: 1000 }); remember(p)
    let r = await a.patch('/RentHistory/CancelInvoice', { invoiceId: ids.I8, reason: 'e2e' })
    ok(r.status === 400 && /Reverse them first/.test(errorText(r)), 'cancel refused while a payment counts', r.json)
    let h = (await a.get('/RentHistory/History')).json.find((y) => y.invoiceId === ids.I8)
    ok(h.hasPaymentRecords === true, 'History list: hasPaymentRecords true')
    await reverse(p, 'Charge entered by mistake')
    h = (await a.get('/RentHistory/History')).json.find((y) => y.invoiceId === ids.I8)
    ok(h.hasPaymentRecords === false, 'History list: hasPaymentRecords false after reversal')
    r = await a.patch('/RentHistory/CancelInvoice', { invoiceId: ids.I8, reason: 'e2e' })
    ok(r.status === 200, 'cancel allowed once every record is reversed', r.json)
    ok(num(db, `SELECT StatusId FROM RentInvoices WHERE Id = ${ids.I8}`) === 6 && one(db, `SELECT COUNT(*) FROM Payments WHERE RentInvoiceId = ${ids.I8}`) === '2',
       'invoice cancelled, both payment rows kept')
    ok(paymentRow(db, p) === originals[p], 'original unchanged')
  }

  section('concurrency')
  {
    const p = await pay(ids.I9, { cash: 7000 }); remember(p)
    const res = await Promise.all(Array.from({ length: 10 }, () => reverse(p, 'race')))
    ok(res.filter((r) => r.status === 200).length === 1 && res.filter((r) => r.status === 400 && /already been reversed/.test(errorText(r))).length === 9,
       '10 parallel reversals: exactly 1 succeeds, 9 refused', res.map((r) => r.status))
    ok(reversalOf(p).length === 1, 'exactly one reversal row')
    const before = bal(ids.I9)
    const p2 = await pay(ids.I9, { cash: 4000 }); remember(p2)
    const [rev, newPay] = await Promise.all([
      reverse(p2, 'race 2'),
      a.post('/Rent/SubmitPayments', [{ tenantId: ids.TA, rentInvoiceId: ids.I9, paymentAmount: 3000, paymentDate: TODAY, paymentMethod: 'Cash', notes: 'race', discountAmount: 0, discountPercent: 0, isLateFeeWaived: false }]),
    ])
    const after = bal(ids.I9)
    const expected = before.balance - 3000
    ok(rev.status === 200 && newPay.status === 200 && after.balance === expected && after.pending === expected,
       'reversal + new payment at the same time: both applied, balance consistent', { rev: rev.status, newPay: newPay.status, after, expected })
    let dupErr = ''
    try { sql(db, `INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, DiscountAmount, DiscountPercent, IsLateFeeWaived, ReversalOfPaymentId) VALUES (${ids.TA}, -7000, GETDATE(), ${ids.I9}, 0, 0, 0, ${p})`) } catch (e) { dupErr = String(e.stdout || e.message) }
    ok(/UX_Payments_ReversalOf|duplicate key/i.test(dupErr), 'database unique index blocks a second reversal row', dupErr.slice(0, 120))
    let chkErr = ''
    try { sql(db, `INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, DiscountAmount, DiscountPercent, IsLateFeeWaived, ReversalOfPaymentId) SELECT ${ids.TA}, 500, GETDATE(), ${ids.I9}, 0, 0, 0, MAX(o.Id) FROM Payments o
      WHERE o.RentInvoiceId = ${ids.I9} AND o.ReversalOfPaymentId IS NULL AND NOT EXISTS (SELECT 1 FROM Payments r WHERE r.ReversalOfPaymentId = o.Id)`) } catch (e) { chkErr = String(e.stdout || e.message) }
    ok(/CK_Payments_Reversal/.test(chkErr), 'database check blocks a reversal row that adds money', chkErr.slice(0, 120))
  }

  section('multi-client isolation')
  {
    const p = await pay(ids.I10, { cash: 2222 }); remember(p)
    // Client B has no payments at all, so the same id can only be found in client A's database
    ok(num(DB.b, `SELECT COUNT(*) FROM Payments`) === 0, "client B's database has no payments")
    const r = await reverse(p, 'cross-client', b)
    ok(r.status === 400 && /not found/.test(errorText(r)), "client B's admin cannot reverse client A's payment", { s: r.status, j: r.json })
    ok(reversalOf(p).length === 0 && paymentRow(db, p) === originals[p], "client A's payment untouched")
    ok(num(DB.b, `SELECT COUNT(*) FROM Payments`) === 0, "client B's database still has no payments")
  }

  section('lease end re-pricing keeps reversed discounts intact')
  {
    const dr = await pay(ids.IB, { disc: 20000, tenant: ids.TB, method: 'Adjustment' }); remember(dr)
    await reverse(dr, 'Discount entered by mistake')
    const keep = await pay(ids.IB, { disc: 9000, tenant: ids.TB, method: 'Adjustment' })
    const moveOut = addDays(monthStart(TODAY), 4) <= TODAY ? addDays(monthStart(TODAY), 4) : monthStart(TODAY)
    const days = Number(moveOut.slice(8)) - 0
    const r = await a.post('/Lease/Terminate', { leaseId: ids.LB, reason: 'E2E move-out', moveOutDate: moveOut })
    ok(r.status === 200, 'lease ended (current month re-priced)', r.json)
    const dim = new Date(Number(TODAY.slice(0, 4)), Number(TODAY.slice(5, 7)), 0).getDate()
    const total = num(db, `SELECT TotalRent FROM RentInvoices WHERE Id = ${ids.IB}`)
    ok(total === Math.round((50000 * days) / dim), `re-priced to ${total} (${days} of ${dim} days)`)
    ok(paymentRow(db, dr) === originals[dr], 'reversed discount row unchanged (20,000)')
    ok(Number(reversalOf(dr)[0][2]) === -20000, 'its reversal row unchanged (-20,000)')
    const keepDisc = num(db, `SELECT DiscountAmount FROM Payments WHERE Id = ${keep}`)
    ok(keepDisc === Math.min(9000, total) && bal(ids.IB).balance === total - keepDisc, `active discount capped to the new amount (${keepDisc})`, keepDisc)
  }

  section('reports show reversals')
  {
    const coll = (await a.get(`/Reports/Collections?from=${TODAY}&to=${TODAY}`)).json
    const rv = coll.find((y) => y.reversalOfPaymentId === fullPay)
    ok(rv && rv.amount === -45000 && rv.discount === -5000 && rv.reversesWaiver === true, 'Collections: reversal line negative on the reversal date, reverses waiver', rv)
    ok(coll.find((y) => y.paymentId === fullPay)?.reversedByPaymentId === rv?.paymentId, 'Collections: original marked as reversed')
    const wr = coll.find((y) => y.reversalOfPaymentId === waiverRow)
    ok(wr && wr.amount === 0 && wr.discount === 0 && wr.reversesWaiver === true, 'Collections: waiver-only reversal has no cash', wr)
    const dr = coll.find((y) => y.reversalOfPaymentId === discRow)
    ok(dr && dr.amount === 0 && dr.discount === -2000 && dr.reversesWaiver === false, 'Collections: discount-only reversal reduces discount only', dr)
    const st = (await a.get(`/Reports/TenantStatement?tenantId=${ids.TA}&from=2000-01-01&to=${TODAY}`)).json
    ok(st.lines.some((l) => /Reversal of payment #/.test(l.description) && l.credit === -45000) && st.lines.some((l) => /Reversal of discount/.test(l.description) && l.credit === -5000),
       'Tenant Statement shows reversal lines (cash and discount)')
    const all = (await a.get(`/Reports/Collections?from=${TODAY}&to=${TODAY}`)).json
    ok(all.find((y) => y.paymentId === fullPay)?.reversedByPaymentId > 0 && all.find((y) => y.paymentId === fullPay)?.reversedAt && all.every((y) => y.enteredAt) && all.some((y) => y.reversalOfPaymentId === fullPay), 'Collections list marks original and reversal (with when and why it was reversed) and when each was entered')
  }

  section('payment window lists the same open invoices as Rent Collection')
  {
    // Make I3 overpaid (25,000 paid on a 20,000 invoice), the way a lease re-pricing can
    sql(db, `UPDATE RentInvoices SET TotalRent = 20000 WHERE Id = ${ids.I3};
             UPDATE ri SET PendingAmount = CASE WHEN b.Balance < 0 THEN 0 ELSE b.Balance END, OverPaidAmount = CASE WHEN b.Balance < 0 THEN -b.Balance ELSE 0 END,
                           StatusId = CASE WHEN b.Balance < 0 THEN 9 WHEN b.Balance = 0 THEN 1 WHEN b.Paid + b.Disc > 0 THEN 8 ELSE 2 END
             FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b WHERE ri.Id = ${ids.I3}`)
    const coll = ((await a.get('/Rent/GetRentCollection')).json || []).filter((x) => x.tenantId === ids.TA).map((x) => x.invoiceId).sort((x, y) => x - y)
    const unpaid = (await a.get(`/Rent/GetUnpaidInvoiceByTenant?tenantId=${ids.TA}`)).json
    const modalIds = (unpaid?.invoices || []).map((x) => x.invoiceId).sort((x, y) => x - y)
    ok(num(db, `SELECT StatusId FROM RentInvoices WHERE Id = ${ids.I3}`) === 9 && modalIds.includes(ids.I3) && JSON.stringify(modalIds) === JSON.stringify(coll),
       'overpaid invoice shows in the payment window too (same list as Rent Collection)', { modalIds, coll })
    ok(unpaid.invoices.find((x) => x.invoiceId === ids.I3)?.remainingAmount === -5000, 'it shows its 5,000 credit (nothing left to pay)')
  }

  section('original records unchanged at the end')
  {
    const changed = Object.entries(originals).filter(([id, r]) => paymentRow(db, id) !== r).map(([id]) => id)
    ok(changed.length === 0, `${Object.keys(originals).length} original payment rows byte-identical`, changed)
  }
}
