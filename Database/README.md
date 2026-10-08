# TRL databases

TRL serves several clients (companies) from one API: **one database per client**, listed in a small **catalog**
database. The API picks a request's database only from the signed login token (client id claim), never from anything
the browser sends.

| Database | Contents | Scripts |
|---|---|---|
| Catalog (e.g. `TRL_Catalog`) | `dbo.Clients`: client code, name, database name, connection key, active flag. No business data, no passwords | `catalog/01_catalog.sql` |
| One per client (e.g. `TRL_DB` = client `TRL`) | All business data: buildings … invoices, payments, maintenance, users, refresh tokens, `SchemaMigrations` | `01`–`06` below + `migrations/` |

Connection strings are **server settings**, not database rows:

```
ConnectionStrings__Catalog      = <catalog connection string>
ClientConnections__<KEY>        = <client database connection string>   (KEY = Clients.ConnectionKey, e.g. TRL)
```

Locally they are in `TRL_API/appsettings.json`; in production set them as environment variables (MonsterASP) or
App Settings / Key Vault (Azure).

## TRL_Tools (run from the TRL_API folder)

```
dotnet run --project TRL_Tools -- status
dotnet run --project TRL_Tools -- migrate [--client CODE] [--dry-run]
dotnet run --project TRL_Tools -- provision --code CODE --name "Company Name" --connection-key KEY --admin-user USER [--admin-email E] [--create-database]
dotnet run --project TRL_Tools -- register --code CODE --name "Company Name" --connection-key KEY
```

The tool reads the same settings as the API (appsettings + environment variables). The admin password comes from
`TRL_ADMIN_PASSWORD` or is asked for (hidden).

- **status** – every client: is its connection configured, is its schema current (and which migration is missing).
- **migrate** – applies the missing migrations of each client database, oldest first, and records them. Stops for a
  client if a migration older than one already applied is missing (out of order), or when a script fails.
- **provision** – a new client: (optionally creates the database), requires it to be **empty**, runs `01`–`06`,
  records all migrations as applied, creates the first Admin (BCrypt) and adds the catalog row. If it fails after
  creating the database itself, it removes it again. Add `ClientConnections__KEY` on the API server before the client
  can log in.
- **register** – adds an existing TRL database (e.g. the original `TRL_DB`) to the catalog; then run `migrate`.

## Schema version tracking (`dbo.SchemaMigrations`)

Each client database records the migrations applied to it (`Name` = file name without `.sql`). The API refuses
logins for a client whose database lacks `SchemaVersion.Required` (`TRL_API/Data/SchemaVersion.cs`) with "This
company's database needs an update", and logs it. `2026-10-08_schema_migrations` created the table and recorded the
earlier migrations after checking their changes were present.

**Adding a migration** (keep all of these in one commit):

1. Add `migrations/<yyyy-MM-dd>[b,c…]_<name>.sql`. Make it safe to re-run, stop with `THROW` before changing anything
   if the data isn't suitable, and end it with
   `IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE Name = N'<file name>') INSERT INTO dbo.SchemaMigrations (Name) VALUES (N'<file name>');`
   so it is recorded even when pasted into a web SQL tool.
2. Apply it to every client: `TRL_Tools migrate` (or paste it per database where the server can't be reached).
3. Set `SchemaVersion.Required` to the new file name (a unit test checks it is the newest file).
4. Regenerate `01`–`04` from a migrated database (`export-schema.ps1`) – new clients are built from these files and
   only *record* the migrations. Old migrations are **not** safe to replay on the newer schema
   (e.g. `2026-09-29d` recreates an older `InvoiceBalance`), so they must never be re-run on an up-to-date database.

## Schema files

Recreate a client database from scratch (SQL Server 2016+) – normally done by `TRL_Tools provision`:

```
sqlcmd -S <server> -d <database> -E -I -f 65001 -i 01_tables.sql -i 02_functions.sql -i 03_views.sql -i 04_foreign_keys.sql -i 06_seed_lookups.sql
```

| File | Contents |
|---|---|
| `01_tables.sql` | Tables, primary keys, unique/other indexes, defaults, check constraints |
| `02_functions.sql` | `dbo.CalculateLateFee` (late fee from days overdue, the invoice's daily rate and cap), `dbo.LeaseMonthCharge` (covered days and prorated rent of a lease in a month), `dbo.InvoiceBalance` (paid, discount, balance and open late fee of an invoice: the one definition every query uses) |
| `03_views.sql` | `dbo.vw_UnitOccupancy` |
| `04_foreign_keys.sql` | Foreign keys (kept separate so table order doesn't matter) |
| `06_seed_lookups.sql` | Lookup rows the code relies on by id (cities, building types, unit/invoice statuses) and the `LateFeeSettings` row (5 due days, 500 per day, max 2 × invoice rent) |

Late fees: `LateFeeSettings` holds the current rules (Late Fee Settings page). Each invoice stores the daily rate and cap in force when it was created (`RentInvoices.LateFeePerDay`, `LateFeeMaxMultiplier`) next to its `DueDate`, so changing the settings only affects invoices generated afterwards.

`sqlcmd` flags: `-f 65001` reads the files as UTF-8 and `-I` enables the quoted-identifier setting that filtered indexes need.

Regenerate files 01–04 after a schema change (needs SSMS 20 installed; edit the server/database name at the top of the script if different):

```
powershell -ExecutionPolicy Bypass -File export-schema.ps1 -OutDir .
```
