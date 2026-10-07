using System.Data;
using TRL_API.Data;

namespace TRL_API.DAL
{
    public class ReminderRepository : IReminderRepository
    {
        private readonly DbHelper _dbHelper;
        public ReminderRepository(DbHelper dbHelper) => _dbHelper = dbHelper;

        // Open invoices (same statuses and balances as Rent Collection) with the tenant's phone, for payment reminders.
        // Balance already includes a late fee that was charged; LateFee is the open late fee not charged yet.
        public async Task<DataTable> GetUnpaidAsync() =>
            await _dbHelper.ExecuteQueryReturnDataTableAsync(@"
                SELECT ri.Id AS InvoiceId, t.TenantId, t.Name AS TenantName, t.Contact AS Phone,
                       bd.BuildingName, f.FloorNumber, u.UnitNumber,
                       ri.InvoiceDate, ri.InvoiceMonth, ri.DueDate, ri.ChargeType,
                       ISNULL(ri.TotalRent, 0) AS Amount,
                       bal.Balance, bal.OpenLateFee AS LateFee
                FROM RentInvoices ri
                INNER JOIN Tenants t ON ri.TenantId = t.TenantId
                LEFT JOIN Units u ON u.UnitId = ISNULL(ri.UnitId, t.UnitId)
                LEFT JOIN Floors f ON u.FloorId = f.FloorId
                LEFT JOIN Buildings bd ON f.BuildingId = bd.BuildingId
                CROSS APPLY dbo.InvoiceBalance(ri.Id, 0) bal
                WHERE ri.StatusId IN (2, 8, 9)
                ORDER BY t.Name, ri.DueDate, ri.Id;");
    }
}
