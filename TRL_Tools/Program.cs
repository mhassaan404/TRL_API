// TRL_Tools: multi-client administration from the command line.
//
//   status                                   every client: database configured? schema up to date?
//   migrate   [--client CODE] [--dry-run]    apply missing migrations, in order, to every client (or one)
//   provision --code C --name "N" --connection-key K --admin-user U [--admin-email E] [--create-database]
//             create a new client database from the TRL schema + all migrations, its first Admin, and the catalog row
//   register  --code C --name "N" --connection-key K
//             add an EXISTING client database (e.g. the original TRL_DB) to the catalog
//
// Settings come from the same places as the API: TRL_API/appsettings.json, appsettings.Development.json and
// environment variables (ConnectionStrings__Catalog, ClientConnections__<key>). The admin password is read from the
// TRL_ADMIN_PASSWORD environment variable or asked for (hidden). Nothing is printed that contains a password.
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

return await Tool.Run(args);

static class Tool
{
    static IConfiguration Config = null!;
    static string DatabaseDir = null!;

    public static async Task<int> Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(File.ReadLines(ThisFile()).TakeWhile(l => l.StartsWith("//")).Aggregate("", (a, l) => a + l.TrimStart('/').TrimStart() + "\n"));
            return 0;
        }
        try
        {
            var repo = FindRepoRoot();
            DatabaseDir = Path.Combine(repo, "Database");
            Config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(repo, "TRL_API", "appsettings.json"), optional: true)
                .AddJsonFile(Path.Combine(repo, "TRL_API", "appsettings.Development.json"), optional: true)
                .AddEnvironmentVariables()
                .Build();
            var opts = Options(args.Skip(1).ToArray());
            return args[0] switch
            {
                "status" => await Status(),
                "migrate" => await Migrate(opts.GetValueOrDefault("client"), opts.ContainsKey("dry-run")),
                "provision" => await Provision(opts),
                "register" => await Register(opts),
                _ => Fail($"Unknown command '{args[0]}'. Run with --help."),
            };
        }
        catch (Exception ex) when (ex is ToolException or SqlException or IOException)
        {
            return Fail(ex.Message);
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    static async Task<int> Status()
    {
        var latest = MigrationFiles().Last().Name;
        Console.WriteLine($"Latest migration: {latest}\n");
        Console.WriteLine($"{"Code",-10} {"Active",-7} {"Database",-24} {"Schema",-10} Details");
        foreach (var c in await Clients())
        {
            var cs = Config[$"ClientConnections:{c.Key}"];
            string schema, details;
            if (string.IsNullOrWhiteSpace(cs)) { schema = "?"; details = $"ClientConnections:{c.Key} is not configured"; }
            else
            {
                try
                {
                    var applied = await Applied(cs);
                    var missing = MigrationFiles().Select(f => f.Name).Where(n => !applied.Contains(n)).ToList();
                    schema = missing.Count == 0 ? "current" : "BEHIND";
                    details = missing.Count == 0 ? "" : $"{missing.Count} missing, first: {missing[0]}";
                }
                catch (SqlException ex) { schema = "ERROR"; details = ex.Message; }
            }
            Console.WriteLine($"{c.Code,-10} {(c.Active ? "yes" : "no"),-7} {c.Database,-24} {schema,-10} {details}");
        }
        return 0;
    }

    static async Task<int> Migrate(string? onlyCode, bool dryRun)
    {
        var clients = (await Clients()).Where(c => onlyCode == null || c.Code.Equals(onlyCode, StringComparison.OrdinalIgnoreCase)).ToList();
        if (clients.Count == 0) throw new ToolException(onlyCode == null ? "The catalog has no clients." : $"Client '{onlyCode}' is not in the catalog.");
        int failed = 0;
        foreach (var c in clients)
        {
            Console.WriteLine($"== {c.Code} ({c.Database})");
            var cs = Config[$"ClientConnections:{c.Key}"];
            if (string.IsNullOrWhiteSpace(cs)) { Console.WriteLine($"   SKIPPED: ClientConnections:{c.Key} is not configured"); failed++; continue; }
            try { await MigrateDatabase(cs, dryRun); }
            catch (Exception ex) when (ex is ToolException or SqlException) { Console.WriteLine("   STOPPED: " + ex.Message); failed++; }
        }
        if (failed > 0) return Fail($"{failed} client(s) were not fully migrated (see above).");
        Console.WriteLine(dryRun ? "\nDry run: nothing was changed." : "\nAll clients are up to date.");
        return 0;
    }

    // Applies the missing migrations of one database, oldest first, recording each in dbo.SchemaMigrations.
    // Refuses when a migration older than an applied one is missing (it would run out of order).
    static async Task MigrateDatabase(string cs, bool dryRun)
    {
        var files = MigrationFiles();
        var applied = await Applied(cs);
        if (applied.Count == 0 && !await TableExists(cs, "SchemaMigrations"))
        {
            // A database from before version tracking: the tracking migration checks the earlier ones are present
            // and records them; it must run first.
            var tracking = files.Where(f => f.Name.EndsWith("_schema_migrations")).Select(f => (Name: f.Name, Path: f.Path)).ToList();
            if (tracking.Count != 1) throw new ToolException("The schema_migrations migration file is missing.");
            Console.WriteLine($"   no version table yet -> {tracking[0].Name} first");
            if (dryRun) return;
            await RunScript(cs, tracking[0].Path);
            applied = await Applied(cs);
        }
        var newestApplied = applied.Max(StringComparer.Ordinal);
        var pending = files.Where(f => !applied.Contains(f.Name)).ToList();
        var outOfOrder = pending.Where(f => newestApplied != null && string.CompareOrdinal(f.Name, newestApplied) < 0).ToList();
        if (outOfOrder.Count > 0)
            throw new ToolException($"{outOfOrder[0].Name} was never applied but newer migrations were ({newestApplied}). " +
                                    "Check that database by hand; nothing was changed.");
        if (pending.Count == 0) { Console.WriteLine("   up to date"); return; }
        foreach (var f in pending)
        {
            Console.WriteLine($"   {(dryRun ? "would apply" : "applying")} {f.Name}");
            if (dryRun) continue;
            await RunScript(cs, f.Path);
            await Exec(cs, "IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = @N) INSERT INTO dbo.SchemaMigrations (Name) VALUES (@N)",
                ("@N", f.Name));
        }
    }

    static async Task<int> Provision(Dictionary<string, string> o)
    {
        var (code, name, key) = (Req(o, "code"), Req(o, "name"), Req(o, "connection-key"));
        var adminUser = Req(o, "admin-user").Trim();
        var adminEmail = o.GetValueOrDefault("admin-email") ?? $"{adminUser}@{code.ToLowerInvariant()}.local";
        CheckCodeAndKey(code, key);
        if (adminUser.Length is < 3 or > 100) throw new ToolException("--admin-user must be 3 to 100 characters.");
        await CheckNotInCatalog(code, key);
        var cs = Config[$"ClientConnections:{key}"]
            ?? throw new ToolException($"Add the connection string first: ClientConnections:{key} (appsettings or environment variable ClientConnections__{key}).");
        var dbName = new SqlConnectionStringBuilder(cs).InitialCatalog;
        if (string.IsNullOrWhiteSpace(dbName)) throw new ToolException($"ClientConnections:{key} has no Database name.");
        var password = AdminPassword();

        var master = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" }.ConnectionString;
        var createdHere = false;
        if (o.ContainsKey("create-database")
            && Convert.ToInt32(await Scalar(master, "SELECT COUNT(*) FROM sys.databases WHERE name = @N", ("@N", dbName))) == 0)
        {
            Console.WriteLine($"Creating database {dbName}...");
            await Exec(master, $"CREATE DATABASE {QuoteName(dbName)}");
            createdHere = true;
        }
        if (Convert.ToInt32(await Scalar(cs, "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0")) > 0)
            throw new ToolException($"Database {dbName} is not empty. Provisioning only uses a new, empty database; nothing was changed.");

        try
        {
            Console.WriteLine($"Applying the TRL schema to {dbName}...");
            foreach (var script in new[] { "01_tables.sql", "02_functions.sql", "03_views.sql", "04_foreign_keys.sql", "06_seed_lookups.sql" })
                await RunScript(cs, Path.Combine(DatabaseDir, script));
            Console.WriteLine("Recording all migrations as applied (the schema scripts already include them)...");
            await RecordAllMigrations(cs);

            Console.WriteLine($"Creating Admin user '{adminUser}'...");
            await Exec(cs, "INSERT INTO dbo.Users (Username, Email, PasswordHash, Role, CreatedAt, IsActive) VALUES (@U, @E, @H, 'Admin', GETDATE(), 1)",
                ("@U", adminUser), ("@E", adminEmail), ("@H", BCrypt.Net.BCrypt.HashPassword(password)));

            await AddToCatalog(code, name, dbName, key);
        }
        catch (Exception ex) when (ex is ToolException or SqlException)
        {
            // Don't leave a half-built client database behind
            if (createdHere)
            {
                SqlConnection.ClearAllPools();
                await Exec(master, $"ALTER DATABASE {QuoteName(dbName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {QuoteName(dbName)}");
                throw new ToolException($"{ex.Message} The new database {dbName} was removed again; nothing else was changed.");
            }
            throw new ToolException($"{ex.Message} Database {dbName} may now be partly set up: empty it (or recreate it) before trying again. The catalog was not changed.");
        }
        Console.WriteLine($"\nClient {code} is ready. Log in with Client Code '{code}' and username '{adminUser}'.");
        Console.WriteLine($"The API server must have ClientConnections__{key} set to this database's connection string.");
        return 0;
    }

    // A new database built from the schema scripts (01-06) already has every migration's changes: the scripts are
    // regenerated from a fully migrated database whenever a migration is added (Database/README.md). So the migrations
    // are only recorded, never replayed (old ones are not safe to run against the newer schema).
    static async Task RecordAllMigrations(string cs)
    {
        if (!await TableExists(cs, "SchemaMigrations"))
            throw new ToolException("The schema scripts have no SchemaMigrations table: regenerate Database/01_tables.sql (export-schema.ps1).");
        foreach (var f in MigrationFiles())
            await Exec(cs, "IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = @N) INSERT INTO dbo.SchemaMigrations (Name) VALUES (@N)",
                ("@N", f.Name));
    }

    static async Task<int> Register(Dictionary<string, string> o)
    {
        var (code, name, key) = (Req(o, "code"), Req(o, "name"), Req(o, "connection-key"));
        CheckCodeAndKey(code, key);
        await CheckNotInCatalog(code, key);
        var cs = Config[$"ClientConnections:{key}"]
            ?? throw new ToolException($"Add the connection string first: ClientConnections:{key}.");
        var dbName = new SqlConnectionStringBuilder(cs).InitialCatalog;
        if (!await TableExists(cs, "Users") || !await TableExists(cs, "RentInvoices"))
            throw new ToolException($"{dbName} doesn't look like a TRL database (no Users/RentInvoices tables). Nothing was changed.");
        await AddToCatalog(code, name, dbName, key);
        var missing = MigrationFiles().Select(f => f.Name).Except(await Applied(cs)).ToList();
        Console.WriteLine($"Client {code} registered for database {dbName}.");
        if (missing.Count > 0)
            Console.WriteLine($"Its schema is behind ({missing.Count} migration(s)); run: TRL_Tools migrate --client {code}");
        return 0;
    }

    // ── Catalog ──────────────────────────────────────────────────────────────

    record ClientRow(int Id, string Code, string Name, string Database, string Key, bool Active);

    static string CatalogCs => Config.GetConnectionString("Catalog")
        ?? throw new ToolException("ConnectionStrings:Catalog is not configured.");

    static async Task<List<ClientRow>> Clients()
    {
        var list = new List<ClientRow>();
        await using var con = new SqlConnection(CatalogCs);
        await con.OpenAsync();
        await using var cmd = new SqlCommand("SELECT ClientId, ClientCode, ClientName, DatabaseName, ConnectionKey, IsActive FROM dbo.Clients ORDER BY ClientId", con);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new ClientRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetBoolean(5)));
        return list;
    }

    static async Task CheckNotInCatalog(string code, string key)
    {
        if (Convert.ToInt32(await Scalar(CatalogCs, "SELECT COUNT(*) FROM dbo.Clients WHERE ClientCode = @C", ("@C", code))) > 0)
            throw new ToolException($"Client code {code} is already in the catalog.");
        if (Convert.ToInt32(await Scalar(CatalogCs, "SELECT COUNT(*) FROM dbo.Clients WHERE ConnectionKey = @K", ("@K", key))) > 0)
            throw new ToolException($"Connection key {key} is already used by another client.");
    }

    static Task AddToCatalog(string code, string name, string db, string key) =>
        Exec(CatalogCs, "INSERT INTO dbo.Clients (ClientCode, ClientName, DatabaseName, ConnectionKey, IsActive) VALUES (@C, @N, @D, @K, 1)",
            ("@C", code), ("@N", name), ("@D", db), ("@K", key));

    static void CheckCodeAndKey(string code, string key)
    {
        if (!Regex.IsMatch(code, "^[A-Za-z0-9_-]{2,20}$")) throw new ToolException("--code: 2-20 letters, digits, _ or -.");
        if (!Regex.IsMatch(key, "^[A-Za-z0-9_]{1,50}$")) throw new ToolException("--connection-key: 1-50 letters, digits or _.");
    }

    // ── SQL helpers ──────────────────────────────────────────────────────────

    static List<(string Name, string Path)> MigrationFiles() =>
        Directory.GetFiles(Path.Combine(DatabaseDir, "migrations"), "*.sql")
            .Select(p => (Name: Path.GetFileNameWithoutExtension(p), Path: p))
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToList();

    static async Task<HashSet<string>> Applied(string cs)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!await TableExists(cs, "SchemaMigrations")) return set;
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Name FROM dbo.SchemaMigrations", con);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetString(0));
        return set;
    }

    static async Task<bool> TableExists(string cs, string table) =>
        Convert.ToInt32(await Scalar(cs, "SELECT COUNT(*) FROM sys.tables WHERE name = @T AND schema_id = SCHEMA_ID('dbo')", ("@T", table))) > 0;

    // Runs a .sql file batch by batch (split at GO lines), stopping at the first error
    static async Task RunScript(string cs, string path)
    {
        var batches = Regex.Split(await File.ReadAllTextAsync(path), @"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(b => !string.IsNullOrWhiteSpace(b));
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        foreach (var batch in batches)
        {
            await using var cmd = new SqlCommand(batch, con) { CommandTimeout = 300 };
            try { await cmd.ExecuteNonQueryAsync(); }
            catch (SqlException ex) { throw new ToolException($"{Path.GetFileName(path)} failed: {ex.Message}"); }
        }
    }

    static async Task<object?> Scalar(string cs, string sql, params (string, object)[] p)
    {
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        await using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        return await cmd.ExecuteScalarAsync();
    }

    static async Task Exec(string cs, string sql, params (string, object)[] p)
    {
        await using var con = new SqlConnection(cs);
        await con.OpenAsync();
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 300 };
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    static string QuoteName(string name) => "[" + name.Replace("]", "]]") + "]";

    // ── Misc ─────────────────────────────────────────────────────────────────

    static string AdminPassword()
    {
        var pw = Environment.GetEnvironmentVariable("TRL_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(pw))
        {
            if (Console.IsInputRedirected) throw new ToolException("Set TRL_ADMIN_PASSWORD (input is redirected, can't prompt).");
            Console.Write("Password for the new Admin: ");
            pw = ReadHidden();
            Console.Write("Repeat it: ");
            if (ReadHidden() != pw) throw new ToolException("The passwords don't match.");
        }
        if (pw.Length < 8) throw new ToolException("The admin password must be at least 8 characters.");
        return pw;
    }

    static string ReadHidden()
    {
        var s = "";
        for (ConsoleKeyInfo k; (k = Console.ReadKey(true)).Key != ConsoleKey.Enter;)
            if (k.Key == ConsoleKey.Backspace) { if (s.Length > 0) s = s[..^1]; }
            else if (!char.IsControl(k.KeyChar)) s += k.KeyChar;
        Console.WriteLine();
        return s;
    }

    static Dictionary<string, string> Options(string[] a)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--")) throw new ToolException($"Unexpected argument '{a[i]}'.");
            var k = a[i][2..];
            d[k] = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
        }
        return d;
    }

    static string Req(Dictionary<string, string> o, string k) =>
        o.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) && v != "true" ? v.Trim() : throw new ToolException($"--{k} is required.");

    // The TRL_API repository root (has Database/migrations), searched upwards from the tool and the current folder
    static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "Database", "migrations"))) return d.FullName;
        throw new ToolException("Can't find the TRL_API folder (Database/migrations). Run the tool from inside the TRL_API repository.");
    }

    static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string p = "") => p;

    static int Fail(string message)
    {
        Console.Error.WriteLine("ERROR: " + message);
        return 1;
    }
}

sealed class ToolException(string message) : Exception(message);
