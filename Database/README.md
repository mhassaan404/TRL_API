# TRL_DB schema

Scripts to recreate the database from scratch (SQL Server 2016+). Run them in order against an empty database:

```
sqlcmd -S <server> -d TRL_DB -E -I -f 65001 -i 01_tables.sql -i 02_functions.sql -i 03_views.sql -i 04_foreign_keys.sql -i 06_seed_lookups.sql
```

| File | Contents |
|---|---|
| `01_tables.sql` | Tables, primary keys, unique/other indexes, defaults, check constraints |
| `02_functions.sql` | `dbo.CalculateLateFee` (late fee from days overdue, the invoice's daily rate and cap), `dbo.LeaseMonthCharge` (covered days and prorated rent of a lease in a month), `dbo.InvoiceBalance` (paid, discount, balance and open late fee of an invoice: the one definition every query uses) |
| `03_views.sql` | `dbo.vw_UnitOccupancy` |
| `04_foreign_keys.sql` | Foreign keys (kept separate so table order doesn't matter) |
| `06_seed_lookups.sql` | Lookup rows the code relies on by id (cities, building types, unit/invoice statuses) and the `LateFeeSettings` row (5 due days, 500 per day, max 2 × invoice rent) |

Late fees: `LateFeeSettings` holds the current rules (Late Fee Settings page). Each invoice stores the daily rate and cap in force when it was created (`RentInvoices.LateFeePerDay`, `LateFeeMaxMultiplier`) next to its `DueDate`, so changing the settings only affects invoices generated afterwards.

No users are seeded. Create the first admin with a BCrypt hash, e.g. generated with `BCrypt.Net.BCrypt.HashPassword("...")`:

```sql
INSERT INTO Users (Username, Email, PasswordHash, Role) VALUES (N'Admin', N'admin@example.com', N'<bcrypt hash>', N'Admin');
```

`migrations/` holds the dated change scripts that were applied to the live database, in date order (each is safe to re-run). Apply them with `sqlcmd ... -I -f 65001 -i <file>`: `-f 65001` reads the files as UTF-8 and `-I` enables the quoted-identifier setting that filtered indexes need. Add one for every schema change, apply it, then regenerate files 01–04.

When you change the schema in the live database, regenerate files 01–04 in the same commit (needs SSMS 20 installed; edit the server name at the top of the script if different):

```
powershell -ExecutionPolicy Bypass -File export-schema.ps1 -OutDir .
```
