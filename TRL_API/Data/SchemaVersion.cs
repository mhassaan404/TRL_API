namespace TRL_API.Data
{
    public static class SchemaVersion
    {
        // The newest migration (Database/migrations, file name without .sql) this API needs in a client database.
        // A client whose database hasn't recorded it in dbo.SchemaMigrations can't log in until it is migrated
        // (TRL_Tools migrate). Update this together with every new migration (a test checks it is the newest file).
        public const string Required = "2026-10-09d_payment_reversal";
    }
}
