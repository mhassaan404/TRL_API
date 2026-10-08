using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class ReportService : IReportService
    {
        // Longest Collections range in one request (about 5 years)
        public const int MaxCollectionsDays = 1830;

        private readonly IReportRepository _dal;
        public ReportService(IReportRepository dal) => _dal = dal;

        public async Task<DataTable> GetArrearsAgeingAsync() => await _dal.GetArrearsAgeingAsync();

        // null = valid, otherwise the message for the user
        private static string? ValidateRange(DateTime? from, DateTime? to, int? maxDays)
        {
            if (from == null || to == null) return "Please choose a From and To date.";
            if (from.Value.Year < 2000 || to.Value.Year > 2100) return "Please choose dates between 2000 and 2100.";
            if (from.Value.Date > to.Value.Date) return "From date must be on or before To date.";
            if (maxDays != null && (to.Value.Date - from.Value.Date).TotalDays > maxDays) return "Please choose a range of 5 years or less.";
            return null;
        }

        public string? ValidateCollectionsRange(DateTime? from, DateTime? to) => ValidateRange(from, to, MaxCollectionsDays);

        public async Task<DataTable> GetCollectionsAsync(DateTime from, DateTime to) =>
            await _dal.GetCollectionsAsync(from.Date, to.Date);

        public async Task<DataTable> GetStatementTenantsAsync() => await _dal.GetStatementTenantsAsync();

        // A statement can cover the tenant's whole history, so there is no length limit
        public string? ValidateStatementRange(DateTime? from, DateTime? to) => ValidateRange(from, to, null);

        // Longest Billing vs Collection range in one request (5 years of months)
        public const int MaxMonths = 60;

        public static DateTime MonthStart(DateTime d) => new(d.Year, d.Month, 1);

        // Month range (any day in a month means that month). null = valid, otherwise the message for the user.
        public string? ValidateMonthRange(DateTime? fromMonth, DateTime? toMonth)
        {
            if (fromMonth == null || toMonth == null) return "Please choose a From and To month.";
            if (fromMonth.Value.Year < 2000 || toMonth.Value.Year > 2100) return "Please choose months between 2000 and 2100.";
            var from = MonthStart(fromMonth.Value);
            var to = MonthStart(toMonth.Value);
            if (from > to) return "From month must be on or before To month.";
            if ((to.Year - from.Year) * 12 + to.Month - from.Month + 1 > MaxMonths) return "Please choose 60 months or less.";
            return null;
        }

        public async Task<DataTable> GetBillingVsCollectionAsync(DateTime fromMonth, DateTime toMonth, int? buildingId) =>
            await _dal.GetBillingVsCollectionAsync(MonthStart(fromMonth), MonthStart(toMonth), buildingId);

        public async Task<DataTable> GetReportBuildingsAsync() => await _dal.GetReportBuildingsAsync();

        public async Task<DataTable> GetRentRollAsync() => await _dal.GetRentRollAsync();

        // Same limits as Collections: 5 years at most
        public string? ValidateMaintenanceRange(DateTime? from, DateTime? to) => ValidateRange(from, to, MaxCollectionsDays);

        public async Task<DataTable> GetMaintenanceCostAsync(DateTime from, DateTime to) =>
            await _dal.GetMaintenanceCostAsync(from.Date, to.Date);

        // null = tenant not found
        public async Task<TenantStatement?> GetTenantStatementAsync(int tenantId, DateTime from, DateTime to)
        {
            var tenant = await _dal.GetStatementTenantAsync(tenantId);
            if (tenant.Rows.Count == 0) return null;
            var t = tenant.Rows[0];

            var statement = new TenantStatement
            {
                TenantId = tenantId,
                TenantName = t["TenantName"] as string ?? "",
                TenantType = t["TenantType"] as string,
                ContactPerson = t["ContactPerson"] as string,
                Contact = t["Contact"] as string,
                Email = t["Email"] as string,
                CnicNtn = t["CnicNtn"] as string,
                Address = t["Address"] as string,
                Units = t["Units"] as string,
                IsDeleted = t["IsDeleted"] is bool d && d,
                OpenLateFee = Convert.ToDecimal(t["OpenLateFee"]),
                AsOfDate = Convert.ToDateTime(t["AsOfDate"]),
            };

            var entries = await _dal.GetStatementEntriesAsync(tenantId, to.Date);
            BuildStatement(statement, entries.AsEnumerable().Select(r => new StatementLine
            {
                Date = Convert.ToDateTime(r["EntryDate"]),
                Type = (string)r["EntryType"],
                InvoiceId = r["InvoiceId"] as int?,
                PaymentId = r["PaymentId"] as int?,
                Description = r["Description"] as string,
                Unit = r["Unit"] as string,
                Method = r["Method"] as string,
                DueDate = r["DueDate"] as DateTime?,
                Charge = Convert.ToDecimal(r["Charge"]),
                Credit = Convert.ToDecimal(r["Credit"]),
            }), from.Date, to.Date);
            return statement;
        }

        // Entries before From make the opening balance; entries From..To are the lines, each with the running balance.
        // Entries must be in date order (the repository sorts them). Entries after To are ignored.
        public static void BuildStatement(TenantStatement s, IEnumerable<StatementLine> entries, DateTime from, DateTime to)
        {
            s.From = from.Date;
            s.To = to.Date;
            s.OpeningBalance = 0;
            s.TotalCharges = 0;
            s.TotalCredits = 0;
            s.Lines = new List<StatementLine>();

            var balance = 0m;
            foreach (var e in entries)
            {
                if (e.Date.Date > s.To) continue;
                balance += e.Charge - e.Credit;
                if (e.Date.Date < s.From)
                {
                    s.OpeningBalance = balance;
                    continue;
                }
                s.TotalCharges += e.Charge;
                s.TotalCredits += e.Credit;
                e.Balance = balance;
                s.Lines.Add(e);
            }
            s.ClosingBalance = s.OpeningBalance + s.TotalCharges - s.TotalCredits;
        }
    }
}
