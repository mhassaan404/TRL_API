namespace TRL_API.Helpers
{
    // The one business date of the system: today in Pakistan (UTC+5, no daylight saving), whatever time zone the
    // server runs in. Same rule as dbo.BusinessToday() in the database (migration 2026-10-12b_business_date).
    // Timestamps (CreatedAt, audit times) are not business dates and are unaffected.
    public static class BusinessDate
    {
        public const int UtcOffsetHours = 5;

        public static DateTime Today => TodayAt(DateTime.UtcNow);

        public static DateTime TodayAt(DateTime utcNow) => utcNow.AddHours(UtcOffsetHours).Date;
    }
}
