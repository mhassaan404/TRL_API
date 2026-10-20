// Reports cross-checked against the database and against each other: Arrears Ageing, Reminders, Rent Collection,
// Collections, Tenant Statement, Billing vs Collection, Occupancy / Rent Roll, Dashboard. Works on whatever the
// earlier areas left in client A, plus its own tenancy, so it can also run alone.
import { DB, TODAY, addDays, apiTenancy, area, monthStart, num, ok, section, sql } from '../lib.mjs'

const parts = (d) => ({ month: Number(d.slice(5, 7)), year: Number(d.slice(0, 4)) })
const round2 = (v) => Math.round(v * 100) / 100
const sum = (rows, f) => round2(rows.reduce((s, r) => s + Number(r[f] || 0), 0))

export default async ({ a }) => {
  area('reports')
  const db = DB.a

  // Own data: last month overdue and part paid with a discount, this month unpaid, a reversed payment
  const t = await apiTenancy(a, db, { start: monthStart(TODAY, -1), rent: 30000 })
  for (const d of [monthStart(TODAY, -1), TODAY]) await a.post('/Rent/GenerateInvoices', { tenantIds: [t.tenantId], ...parts(d) })
  const [prev, cur] = sql(db, `SELECT Id FROM RentInvoices WHERE TenantId = ${t.tenantId} ORDER BY InvoiceMonth`).map((r) => Number(r[0]))
  const payBody = (inv, cash, disc = 0) => [{ tenantId: t.tenantId, rentInvoiceId: inv, paymentAmount: cash, paymentDate: TODAY, paymentMethod: 'Cash', discountAmount: disc, discountPercent: 0, isLateFeeWaived: false }]
  await a.post('/Rent/SubmitPayments', payBody(prev, 10000, 1000))
  await a.post('/Rent/SubmitPayments', payBody(cur, 7000))
  await a.post('/Rent/ReversePayment', { paymentId: num(db, `SELECT MAX(Id) FROM Payments WHERE RentInvoiceId = ${cur}`), reason: 'e2e report check' })

  section('open invoices: Arrears Ageing = Rent Collection = Reminders = database')
  {
    const dbOpen = sql(db, `SELECT ri.Id, b.Balance + b.OpenLateFee FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                            WHERE ri.StatusId IN (2, 8, 9) AND b.Balance + b.OpenLateFee <> 0`)
    const dbOwed = round2(dbOpen.reduce((s, r) => s + Number(r[1]), 0))
    const arrears = (await a.get('/Reports/ArrearsAgeing')).json
    ok(arrears.length === dbOpen.length && sum(arrears, 'owed') === dbOwed, `Arrears Ageing: ${dbOpen.length} invoices, owed ${dbOwed}`, { n: arrears.length, owed: sum(arrears, 'owed') })
    ok(arrears.every((r) => round2(Number(r.balance) + Number(r.lateFee)) === Number(r.owed)), 'every arrears row: owed = balance + open late fee')
    ok(arrears.every((r) => (r.dueDate.slice(0, 10) < TODAY ? r.daysOverdue > 0 : r.daysOverdue === 0)), 'days overdue only after the due date')
    const coll = (await a.get('/Rent/GetRentCollection')).json
    const collOwed = round2(coll.reduce((s, r) => s + Number(r.remainingAmount) + Number(r.lateFee), 0))
    ok(collOwed === round2(sum(arrears, 'owed') + sql(db, `SELECT ISNULL(SUM(b.Balance + b.OpenLateFee), 0) FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                                     WHERE ri.StatusId IN (2, 8, 9) AND b.Balance + b.OpenLateFee = 0`).map((r) => Number(r[0]))[0]),
       'Rent Collection owed total = Arrears Ageing total', { collOwed, arrears: sum(arrears, 'owed') })
    const rem = (await a.get('/Reminders/GetUnpaid')).json
    const remIds = new Set(rem.map((r) => r.invoiceId))
    ok(arrears.filter((r) => Number(r.owed) > 0).every((r) => remIds.has(r.invoiceId)), 'every invoice with money owed is in Reminders')
    ok(rem.some((r) => r.invoiceId === cur) && arrears.some((r) => r.invoiceId === cur && Number(r.owed) >= 30000), 'reversed payment: invoice owed in full again in Arrears and Reminders')
  }

  section('Collections = payments by date (deposit applications excluded)')
  {
    const from = monthStart(TODAY, -24)
    const coll = (await a.get(`/Reports/Collections?from=${from}&to=${TODAY}`)).json
    const [dbCash, dbDisc, dbRows] = sql(db, `SELECT ISNULL(SUM(PaymentAmount), 0), ISNULL(SUM(DiscountAmount), 0), COUNT(*) FROM Payments
                                              WHERE PaymentDate >= '${from}' AND PaymentDate < DATEADD(DAY, 1, '${TODAY}') AND ISNULL(PaymentMethod, N'') NOT IN (N'Security Deposit', N'Credit to Deposit')`)[0].map(Number)
    ok(coll.length === dbRows && sum(coll, 'amount') === dbCash && sum(coll, 'discount') === dbDisc, `received ${dbCash}, discounts ${dbDisc}, ${dbRows} rows`, { n: coll.length, amount: sum(coll, 'amount') })
    let r = await a.get(`/Reports/Collections?from=${TODAY}&to=${addDays(TODAY, -1)}`)
    ok(r.status === 400, 'reversed date range refused', r.status)
    r = await a.get('/Reports/Collections')
    ok(r.status === 400, 'missing dates refused', r.status)
  }

  section('Tenant Statement closing balance = open invoices')
  {
    const tenants = sql(db, `SELECT DISTINCT TenantId FROM RentInvoices`).map((r) => Number(r[0]))
    const bad = []
    for (const id of tenants) {
      const st = (await a.get(`/Reports/TenantStatement?tenantId=${id}&from=2000-01-01&to=${TODAY}`)).json
      const expected = num(db, `SELECT ISNULL(SUM(b.Balance), 0) FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b WHERE ri.TenantId = ${id} AND ri.StatusId <> 6 AND ISNULL(ri.InvoiceDate, ri.CreatedAt) <= '${TODAY}'`)
        - num(db, `SELECT ISNULL(SUM(PaymentAmount + DiscountAmount), 0) FROM Payments WHERE TenantId = ${id} AND RentInvoiceId IS NULL`)
      const lines = round2((st.lines || []).reduce((s, l) => s + Number(l.charge) - Number(l.credit), Number(st.openingBalance)))
      if (Math.abs(st.closingBalance - expected) > 0.005 || Math.abs(lines - st.closingBalance) > 0.005) bad.push({ id, closing: st.closingBalance, expected, lines })
    }
    ok(bad.length === 0, `${tenants.length} tenants: closing = sum of balances of invoices dated up to today = opening + lines`, bad)
    let r = await a.get(`/Reports/TenantStatement?tenantId=99999999&from=2000-01-01&to=${TODAY}`)
    ok(r.status === 404, 'unknown tenant -> 404', r.status)
    r = await a.get(`/Reports/TenantStatement?tenantId=${t.tenantId}&from=${TODAY}&to=2000-01-01`)
    ok(r.status === 400, 'reversed range -> 400', r.status)
  }

  section('Billing vs Collection = invoices and cash by month')
  {
    const from = monthStart(TODAY, -12)
    const to = monthStart(TODAY, 1)
    const rows = (await a.get(`/Reports/BillingVsCollection?fromMonth=${from}&toMonth=${to}`)).json
    const [billed, collected, disc] = sql(db, `SELECT ISNULL(SUM(ri.TotalRent + ri.LateFeeCharged), 0), ISNULL(SUM(b.Paid), 0), ISNULL(SUM(b.Disc), 0) FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                                               WHERE ri.StatusId <> 6 AND ri.InvoiceMonth BETWEEN '${from}' AND '${to}'`)[0].map(Number)
    ok(rows.length === 14 && sum(rows, 'totalBilled') === billed && sum(rows, 'collected') === collected && sum(rows, 'discounts') === disc,
       `14 months; billed ${billed}, collected ${collected}, discounts ${disc}`, { n: rows.length, billed: sum(rows, 'totalBilled'), collected: sum(rows, 'collected') })
    const cash = num(db, `SELECT ISNULL(SUM(p.PaymentAmount), 0) FROM Payments p JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                          WHERE p.PaymentDate >= '${from}' AND p.PaymentDate < DATEADD(MONTH, 1, '${to}') AND ri.StatusId <> 6
                            AND ISNULL(p.PaymentMethod, N'') NOT IN (N'Security Deposit', N'Credit to Deposit')`)
    ok(sum(rows, 'cashReceived') === cash, `cash received by payment month = ${cash}`, sum(rows, 'cashReceived'))
    const r = await a.get(`/Reports/BillingVsCollection?fromMonth=${monthStart(TODAY, -70)}&toMonth=${to}`)
    ok(r.status === 400, 'more than 60 months refused', r.status)
  }

  section('Occupancy / Rent Roll and Dashboard')
  {
    const roll = (await a.get('/Reports/RentRoll')).json
    const occupiedDb = num(db, `SELECT COUNT(DISTINCT u.UnitId) FROM Units u JOIN Floors f ON f.FloorId = u.FloorId JOIN Buildings b ON b.BuildingId = f.BuildingId
                                JOIN TenantLeases l ON l.UnitId = u.UnitId AND l.IsActive = 1 AND l.StartDate <= dbo.BusinessToday()
                                WHERE u.IsActive = 1 AND f.IsActive = 1 AND b.IsActive = 1`)
    ok(roll.filter((r) => r.isOccupied).length >= occupiedDb, `occupied units include every unit with a started active lease (${occupiedDb})`, roll.filter((r) => r.isOccupied).length)
    const mine = roll.find((r) => r.unitId === t.unitId)
    ok(mine && mine.isOccupied && Number(mine.arrears) === num(db, `SELECT SUM(CASE WHEN b.Balance + b.OpenLateFee > 0 THEN b.Balance + b.OpenLateFee ELSE 0 END)
                                                             FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b WHERE ri.UnitId = ${t.unitId} AND ri.StatusId IN (2, 8, 9)`),
       'rent roll arrears of a unit = its open invoices', mine)
    const dash = (await a.get('/Dashboard/dashboard')).json
    const thisMonth = dash.find((m) => m.monthYear === TODAY.slice(0, 7))
    const [due, paid] = sql(db, `SELECT ISNULL(SUM(ri.TotalRent + ri.LateFeeCharged), 0), ISNULL(SUM(b.Paid), 0) FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b
                                 WHERE ri.StatusId <> 6 AND YEAR(ri.InvoiceDate) = ${TODAY.slice(0, 4)} AND MONTH(ri.InvoiceDate) = ${Number(TODAY.slice(5, 7))}`)[0].map(Number)
    ok(dash.length === 7 && thisMonth && Number(thisMonth.totalRentDue) === due && Number(thisMonth.collectedAmount) === paid, `dashboard this month: due ${due}, collected ${paid}`, thisMonth)
  }

  section('Tenant Statement: a rent change at lease end is its own dated line')
  {
    const rent = 31000
    const tl = await apiTenancy(a, db, { start: monthStart(TODAY, -1), rent })
    await a.post('/Rent/GenerateInvoices', { tenantIds: [tl.tenantId], ...parts(TODAY) }) // whole month billed
    const inv = num(db, `SELECT MAX(Id) FROM RentInvoices WHERE LeaseId = ${tl.leaseId} AND InvoiceMonth = '${monthStart(TODAY)}'`)
    ok(num(db, `SELECT TotalRent FROM RentInvoices WHERE Id = ${inv}`) === rent, 'this month billed in full')
    let r = await a.post('/Lease/Terminate', { leaseId: tl.leaseId, reason: 'E2E early move-out', moveOutDate: monthStart(TODAY) })
    const now = num(db, `SELECT TotalRent FROM RentInvoices WHERE Id = ${inv}`)
    ok(r.status === 200 && now < rent, `ended on the 1st: invoice re-priced to one day (${now})`, r.json)
    ok(sql(db, `SELECT OldAmount, Amount FROM InvoiceAudit WHERE InvoiceId = ${inv} AND Action = 'RENT_ADJUSTED'`)[0]?.map(Number).join('|') === `${rent}|${now}`,
       'the change records the old and the new amount')
    const st = (await a.get(`/Reports/TenantStatement?tenantId=${tl.tenantId}&from=2000-01-01&to=${TODAY}`)).json
    const lines = st?.lines || []
    const invLine = lines.find((l) => l.type === 'Invoice' && l.invoiceId === inv)
    const adjLine = lines.find((l) => l.type === 'Adjustment' && l.invoiceId === inv)
    ok(invLine && Number(invLine.charge) === rent && !/Rent .* – Rent /.test(invLine.description),
       'invoice shown at the amount billed on its date (no repeated "Rent")', invLine)
    ok(adjLine && Number(adjLine.credit) === round2(rent - now) && Number(adjLine.charge) === 0 && /Rent reduced/.test(adjLine.description),
       `the reduction is its own line (${round2(rent - now)} credit)`, adjLine)
    const owed = num(db, `SELECT ISNULL(SUM(b.Balance), 0) FROM RentInvoices ri CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) b WHERE ri.TenantId = ${tl.tenantId} AND ri.StatusId <> 6`)
    ok(Number(st?.closingBalance) === owed, `closing balance still = what is owed (${owed})`, st?.closingBalance)
    // Up to the day before the change, the balance is the full month (what it really was then)
    const before = (await a.get(`/Reports/TenantStatement?tenantId=${tl.tenantId}&from=2000-01-01&to=${monthStart(TODAY)}`)).json
    const beforeInv = (before?.lines || []).find((l) => l.type === 'Invoice' && l.invoiceId === inv)
    ok(beforeInv && Number(beforeInv.balance) >= rent, 'running balance right after the invoice includes the full month', beforeInv)
  }

  section('payment without an invoice: building comes from the tenant\'s lease')
  {
    const building = num(db, `SELECT f.BuildingId FROM Units u JOIN Floors f ON f.FloorId = u.FloorId WHERE u.UnitId = ${t.unitId}`)
    const month = monthStart(TODAY)
    const cashOf = async () => sum((await a.get(`/Reports/BillingVsCollection?fromMonth=${month}&toMonth=${month}&buildingId=${building}`)).json, 'cashReceived')
    const before = await cashOf()
    // Older rows can have no invoice (RentInvoiceId NULL); removed again below
    const id = num(db, `INSERT INTO Payments (TenantId, PaymentAmount, PaymentDate, RentInvoiceId, PaymentMethod, Notes, DiscountAmount, DiscountPercent, IsLateFeeWaived, CreatedBy, CreatedAt)
      OUTPUT inserted.Id VALUES (${t.tenantId}, 123.45, '${TODAY}', NULL, N'Cash', N'e2e no invoice', 0, 0, 0, 1, GETDATE())`)
    try {
      const after = await cashOf()
      ok(round2(after - before) === 123.45, 'Billing vs Collection (building filter) counts it as cash received', { before, after })
      const row = ((await a.get(`/Reports/Collections?from=${TODAY}&to=${TODAY}`)).json || []).find((p) => p.paymentId === id)
      ok(row && row.unitNumber && row.buildingName, 'Collections shows its building and unit', row)
    } finally {
      sql(db, `DELETE FROM Payments WHERE Id = ${id}`)
    }
  }

  section('report access needs a login')
  {
    const r = await a.get('/Reports/ArrearsAgeing', { anonymous: true })
    ok(r.status === 401, 'anonymous -> 401', r.status)
    ok(!Array.isArray(r.json), 'no report rows returned to anonymous callers', r.json)
  }
}
