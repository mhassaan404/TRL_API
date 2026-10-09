using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.DAL
{
    public class RentHistoryRepository : IRentHistoryRepository
    {
        private readonly DbHelper _dbHelper;

        public RentHistoryRepository(DbHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }


        public async Task<DataTable> GetHistoryAsync()
        {
            string query = @"
                SELECT ri.Id AS invoiceId, ri.LeaseId, ri.InvoiceDate, ri.DueDate, t.Name AS Tenant,
                       bd.BuildingName, f.FloorNumber, u.UnitNumber AS Unit,
                       ri.TotalRent AS MonthlyRent, ri.LateFeeCharged, ri.ChargeType,
                       bal.Paid AS PaidAmount, bal.Disc AS DiscountAmount,
                       CASE WHEN ri.StatusId = 6 THEN 0 ELSE bal.Balance END AS Balance,
                       bal.LastPaymentDate,
                       COALESCE(pt.Methods, 'N/A') AS PaymentMethod,
                       COALESCE(sl.StatusName, 'Unknown') AS Status,
                       -- Any payment record that still counts (payment, discount, waiver or adjustment, not reversed):
                       -- CancelInvoice refuses these
                       CAST(CASE WHEN " + InvoiceSql.HasActivePaymentRecords + @" THEN 1 ELSE 0 END AS BIT) AS HasPaymentRecords,
                       -- Same rules as ReinstateInvoice: the lease still covers that month, and no other rent
                       -- invoice for that lease and month is open (UX_RentInvoices_RentPerLeaseMonth)
                       CAST(CASE WHEN ri.StatusId = 6
                                  AND (ri.LeaseId IS NULL OR ri.ChargeType IS NOT NULL OR rl.BilledThrough IS NULL
                                       OR ri.InvoiceMonth < DATEFROMPARTS(YEAR(rl.BilledThrough), MONTH(rl.BilledThrough), 1)
                                       OR rc.Amount > 0)
                                  AND NOT (ri.LeaseId IS NOT NULL AND ri.ChargeType IS NULL AND EXISTS (
                                       SELECT 1 FROM RentInvoices o WHERE o.LeaseId = ri.LeaseId AND o.InvoiceMonth = ri.InvoiceMonth
                                         AND o.ChargeType IS NULL AND o.StatusId <> 6 AND o.Id <> ri.Id))
                            THEN 1 ELSE 0 END AS BIT) AS CanReinstate
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON u.FloorId = f.FloorId
                LEFT JOIN Buildings bd ON f.BuildingId = bd.BuildingId
                LEFT JOIN TenantLeases rl ON rl.LeaseId = ri.LeaseId
                OUTER APPLY (SELECT Amount FROM dbo.LeaseMonthCharge(ri.LeaseId, ri.InvoiceMonth)) rc
                LEFT JOIN StatusList sl ON ri.StatusId = sl.StatusId
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                OUTER APPLY (
                    SELECT STUFF((SELECT DISTINCT ', ' + p2.PaymentMethod FROM Payments p2
                                  WHERE p2.RentInvoiceId = ri.Id AND p2.PaymentMethod IS NOT NULL
                                  FOR XML PATH('')), 1, 2, '') AS Methods
                ) pt
                ORDER BY ri.InvoiceDate DESC, ri.Id DESC;";
            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query);
        }

        // Everything about one invoice for the History window (read only): the invoice and its totals, each payment
        // record with the balance after it, and the recorded events (cancel, reinstate, rent adjusted on lease
        // end/renewal, discount reduced, late fee reversed). Returns (invoice, payments, events); invoice is empty
        // when the id doesn't exist.
        public async Task<(DataTable Invoice, DataTable Payments, DataTable Events, DataTable Charges)> GetInvoiceDetailsAsync(int invoiceId)
        {
            var id = new SqlParameter("@InvoiceId", invoiceId);

            var invoice = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT ri.Id AS InvoiceId, ri.TenantId, t.Name AS TenantName, b.BuildingName, f.FloorNumber, u.UnitNumber,
                       ri.LeaseId, ri.InvoiceDate, ri.DueDate, ri.InvoiceMonth, ri.CreatedAt,
                       ri.TotalRent, ri.Description, ri.ChargeType, COALESCE(sl.StatusName, 'Unknown') AS Status,
                       ri.LateFeeCharged, ri.LateFeeChargedAt, ri.LateFeePerDay, ri.LateFeeMaxMultiplier,
                       bal.Paid, bal.Disc AS Discount, bal.Waived AS LateFeeWaived, bal.OpenLateFee,
                       bal.RentBalance, bal.Balance,
                       -- An extra charge can point to the invoice it relates to (e.g. a Rent Correction)
                       ri.RelatedInvoiceId, rel.InvoiceDate AS RelatedInvoiceDate, rel.TotalRent AS RelatedAmount,
                       rel.ChargeType AS RelatedChargeType, rsl.StatusName AS RelatedStatus
                FROM RentInvoices ri
                INNER JOIN Tenants t ON t.TenantId = ri.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON f.FloorId = u.FloorId
                LEFT JOIN Buildings b ON b.BuildingId = f.BuildingId
                LEFT JOIN StatusList sl ON sl.StatusId = ri.StatusId
                LEFT JOIN RentInvoices rel ON rel.Id = ri.RelatedInvoiceId
                LEFT JOIN StatusList rsl ON rsl.StatusId = rel.StatusId
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                WHERE ri.Id = @InvoiceId;", new[] { id });

            // Balance after each record = rent + charged late fee - everything paid/discounted up to that record.
            // Reversals: ReversalOfPaymentId on the reversal row; ReversedBy* on the original row (reason, who, when).
            // ReverseBlock = why the row can't be reversed (NULL = Reverse allowed), the same rule the reversal checks.
            var payments = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT p.Id AS PaymentId, p.PaymentDate, p.PaymentAmount, p.DiscountAmount, p.DiscountPercent,
                       p.IsLateFeeWaived, p.PaymentMethod, p.Notes, p.CreatedAt, cu.Username AS CreatedBy,
                       p.UpdatedAt, uu.Username AS UpdatedBy, p.ReversalOfPaymentId,
                       rv.Id AS ReversedByPaymentId, rv.CreatedAt AS ReversedAt, rvu.Username AS ReversedBy, rv.Notes AS ReversalReason,
                       ri.TotalRent + ri.LateFeeCharged
                         - SUM(p.PaymentAmount + p.DiscountAmount) OVER (ORDER BY p.PaymentDate, p.Id
                                                                         ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS BalanceAfter,
                       " + InvoiceSql.ReversalBlock + @" AS ReverseBlock
                FROM Payments p
                INNER JOIN RentInvoices ri ON ri.Id = p.RentInvoiceId
                LEFT JOIN Users cu ON cu.UserId = p.CreatedBy
                LEFT JOIN Users uu ON uu.UserId = p.UpdatedBy
                LEFT JOIN Payments rv ON rv.ReversalOfPaymentId = p.Id
                LEFT JOIN Users rvu ON rvu.UserId = rv.CreatedBy
                WHERE p.RentInvoiceId = @InvoiceId
                ORDER BY p.PaymentDate, p.Id;", new[] { new SqlParameter("@InvoiceId", invoiceId) });

            var events = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT a.Id AS EventId, a.CreatedAt, a.Action, a.Amount, a.Reason, u.Username AS CreatedBy
                FROM InvoiceAudit a
                LEFT JOIN Users u ON u.UserId = a.CreatedBy
                WHERE a.InvoiceId = @InvoiceId
                ORDER BY a.CreatedAt, a.Id;", new[] { new SqlParameter("@InvoiceId", invoiceId) });

            // Extra charges that point to this invoice
            var charges = await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT c.Id AS InvoiceId, c.InvoiceDate, c.ChargeType, c.Description, c.TotalRent,
                       COALESCE(sl.StatusName, 'Unknown') AS Status
                FROM RentInvoices c
                LEFT JOIN StatusList sl ON sl.StatusId = c.StatusId
                WHERE c.RelatedInvoiceId = @InvoiceId
                ORDER BY c.Id;", new[] { new SqlParameter("@InvoiceId", invoiceId) });

            return (invoice, payments, events, charges);
        }

        // Only allowed when the invoice has no payment record that still counts: none at all, or every one reversed
        // (the records and their reversals stay). The invoice row is locked first, so a payment or reversal can't
        // slip in between the check and the cancel.
        public async Task<DataTable> CancelInvoice(int invoiceId, string reason, int userId)
        {
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @r VARCHAR(20), @Locked INT;
                BEGIN TRAN;
                SELECT @Locked = Id FROM RentInvoices WITH (UPDLOCK, HOLDLOCK) WHERE Id = @InvoiceId AND StatusId <> 6;
                IF @Locked IS NULL
                    SET @r = 'NOT_FOUND';
                ELSE IF EXISTS (SELECT 1 FROM RentInvoices ri WHERE ri.Id = @InvoiceId AND " + InvoiceSql.HasActivePaymentRecords + @")
                    SET @r = 'HAS_PAYMENTS';
                ELSE
                BEGIN
                    UPDATE RentInvoices SET StatusId = 6 WHERE Id = @InvoiceId;
                    INSERT INTO InvoiceAudit(InvoiceId, Action, Reason, CreatedBy)
                    VALUES (@InvoiceId, 'CANCELLED', @Reason, @UserId);
                    SET @r = 'OK';
                END
                COMMIT;
                SELECT @r AS Result;";

            return await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[]
            {
                new SqlParameter("@InvoiceId", invoiceId),
                new SqlParameter("@Reason", reason),
                new SqlParameter("@UserId", userId),
            });
        }

        public async Task<ApiResponse> ReinstateInvoice(int invoiceId)
        {
            // A lease rent invoice for a month at/after the lease's billing end can only come back if the lease still
            // covers part of that month, and is re-priced to what it covered (so a month after move-out can't return).
            string query = @"
                SET XACT_ABORT ON;
                DECLARE @Ok BIT = 0, @NewAmount DECIMAL(18,2), @Descr NVARCHAR(200), @Reprice BIT = 0;
                SELECT @Ok = CASE WHEN ri.LeaseId IS NULL OR ri.ChargeType IS NOT NULL OR l.BilledThrough IS NULL
                                       OR ri.InvoiceMonth < DATEFROMPARTS(YEAR(l.BilledThrough), MONTH(l.BilledThrough), 1)
                                       OR c.Amount > 0 THEN 1 ELSE 0 END,
                       @Reprice = CASE WHEN ri.LeaseId IS NOT NULL AND ri.ChargeType IS NULL AND l.BilledThrough IS NOT NULL
                                       AND ri.InvoiceMonth >= DATEFROMPARTS(YEAR(l.BilledThrough), MONTH(l.BilledThrough), 1)
                                       THEN 1 ELSE 0 END,
                       @NewAmount = c.Amount, @Descr = c.Descr
                FROM RentInvoices ri
                LEFT JOIN TenantLeases l ON l.LeaseId = ri.LeaseId
                OUTER APPLY dbo.LeaseMonthCharge(ri.LeaseId, ri.InvoiceMonth) c
                WHERE ri.Id = @InvoiceId AND ri.StatusId = 6;

                IF @Ok = 0 BEGIN SELECT 'NOT_ALLOWED' AS Result; RETURN; END

                BEGIN TRAN;
                IF @Reprice = 1
                    UPDATE RentInvoices SET TotalRent = @NewAmount, Description = @Descr WHERE Id = @InvoiceId;

                INSERT INTO InvoiceAudit(InvoiceId, Action)
                SELECT Id, 'REINSTATED' FROM RentInvoices WHERE Id = @InvoiceId AND StatusId = 6;

                " + InvoiceSql.Recalc + @"
                WHERE ri.Id = @InvoiceId AND ri.StatusId = 6;
                COMMIT;
                SELECT 'OK' AS Result;";

            // Explicit result: the lookup SELECT above would make a row-count based success check unreliable
            var dt = await _dbHelper.ExecuteQueryReturnDataTableAsync(query, new[] { new SqlParameter("@InvoiceId", invoiceId) });
            return new ApiResponse { IsSuccess = dt.Rows.Count > 0 && dt.Rows[0]["Result"].ToString() == "OK" };
        }
    }
}
