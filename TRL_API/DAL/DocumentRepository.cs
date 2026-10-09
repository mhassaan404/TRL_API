using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;

namespace TRL_API.DAL
{
    // Printable documents (payment receipts, invoices): read only.
    public class DocumentRepository : IDocumentRepository
    {
        private readonly DbHelper _dbHelper;
        public DocumentRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        // One row per payment: the payment, its invoice, tenant and unit (with the building's address).
        // BalanceAfter = what was still owed on the invoice right after this payment (same running balance as the
        // invoice History). CurrentBalance = what is owed on the invoice today.
        public async Task<DataTable> GetReceiptsAsync(IReadOnlyList<int> paymentIds)
        {
            var names = paymentIds.Select((_, i) => $"@P{i}").ToArray();
            var sql = $@"
                ;WITH Running AS (
                    SELECT p.Id,
                           ri.TotalRent + ri.LateFeeCharged
                             - SUM(p.PaymentAmount + p.DiscountAmount) OVER (PARTITION BY p.RentInvoiceId ORDER BY p.PaymentDate, p.Id
                                                                           ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS BalanceAfter
                    FROM Payments p
                    INNER JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                    WHERE p.RentInvoiceId IN (SELECT RentInvoiceId FROM Payments WHERE Id IN ({string.Join(", ", names)}))
                )
                SELECT p.Id AS PaymentId, p.PaymentDate, p.PaymentAmount, p.DiscountAmount, p.IsLateFeeWaived, p.PaymentMethod,
                       p.Notes, p.CreatedAt, cu.Username AS RecordedBy, p.ReversalOfPaymentId,
                       t.TenantId, t.Name AS TenantName, t.TenantType, t.ContactPerson, t.Contact, t.Email, t.CnicNtn,
                       t.Address AS TenantAddress,
                       ri.Id AS InvoiceId, ri.InvoiceDate, ri.InvoiceMonth, ri.DueDate, ri.ChargeType, ri.Description,
                       ri.TotalRent, ri.LateFeeCharged, ri.LeaseId, ri.StatusId AS InvoiceStatusId,
                       COALESCE(sl.StatusName, 'Unknown') AS InvoiceStatus,
                       b.BuildingName, b.Address AS BuildingAddress, f.FloorNumber, u.UnitNumber,
                       r.BalanceAfter, bal.Balance AS CurrentBalance,
                       -- A reversed payment's receipt stays printable but is marked REVERSED (entered by mistake)
                       rv.Id AS ReversedByPaymentId, rv.CreatedAt AS ReversedAt, rvu.Username AS ReversedBy, rv.Notes AS ReversalReason
                FROM Payments p
                LEFT JOIN Payments rv ON rv.ReversalOfPaymentId = p.Id
                LEFT JOIN Users rvu ON rvu.UserId = rv.CreatedBy
                INNER JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                INNER JOIN Tenants t ON t.TenantId = ri.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN StatusList sl ON sl.StatusId = ri.StatusId
                LEFT JOIN Users cu ON cu.UserId = p.CreatedBy
                INNER JOIN Running r ON r.Id = p.Id
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                WHERE p.Id IN ({string.Join(", ", names)})
                ORDER BY p.Id;";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(sql,
                paymentIds.Select((id, i) => new SqlParameter($"@P{i}", id)).ToArray());
        }

        // The invoice with tenant, unit (building address), lease and today's balance.
        // OpenLateFee = late fee building up on the unpaid amount today but not charged yet.
        public async Task<DataTable> GetInvoiceAsync(int invoiceId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT ri.Id AS InvoiceId, ri.InvoiceDate, ri.InvoiceMonth, ri.DueDate, ri.CreatedAt, ri.ChargeType, ri.Description,
                       ri.TotalRent, ri.LateFeeCharged, ri.LateFeeChargedAt, ri.LateFeePerDay, ri.LateFeeMaxMultiplier,
                       ri.StatusId, COALESCE(sl.StatusName, 'Unknown') AS Status,
                       ri.RelatedInvoiceId, rel.InvoiceMonth AS RelatedInvoiceMonth, rel.ChargeType AS RelatedChargeType,
                       ri.LeaseId, l.StartDate AS LeaseStartDate, l.EndDate AS LeaseEndDate, l.RentAmount AS LeaseRent,
                       t.TenantId, t.Name AS TenantName, t.TenantType, t.ContactPerson, t.Contact, t.Email, t.CnicNtn,
                       t.Address AS TenantAddress,
                       b.BuildingName, b.Address AS BuildingAddress, f.FloorNumber, u.UnitNumber,
                       bal.Paid, bal.Disc AS Discount, bal.Waived AS LateFeeWaived, bal.OpenLateFee, bal.Balance,
                       CAST(GETUTCDATE() AS DATE) AS AsOfDate
                FROM RentInvoices ri
                INNER JOIN Tenants t ON t.TenantId = ri.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN StatusList sl ON sl.StatusId = ri.StatusId
                LEFT JOIN TenantLeases l ON l.LeaseId = ri.LeaseId
                LEFT JOIN RentInvoices rel ON rel.Id = ri.RelatedInvoiceId
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                WHERE ri.Id = @InvoiceId;",
                new[] { new SqlParameter("@InvoiceId", invoiceId) });

        // Everything recorded against the invoice, oldest first (cash, discounts, waivers, adjustments and reversals)
        public async Task<DataTable> GetInvoicePaymentsAsync(int invoiceId) =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT p.Id AS PaymentId, p.PaymentDate, p.PaymentAmount, p.DiscountAmount, p.IsLateFeeWaived, p.PaymentMethod,
                       p.ReversalOfPaymentId,
                       (SELECT rv.Id FROM Payments rv WHERE rv.ReversalOfPaymentId = p.Id) AS ReversedByPaymentId
                FROM Payments p
                WHERE p.RentInvoiceId = @InvoiceId
                ORDER BY p.PaymentDate, p.Id;",
                new[] { new SqlParameter("@InvoiceId", invoiceId) });
    }
}
