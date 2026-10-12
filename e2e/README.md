# TRL end-to-end tests

Runs the real API against throwaway databases and checks every result over HTTP **and** in the database.
No npm packages: Node 18+ (`fetch`) and `sqlcmd` only.

```
node e2e/run.mjs                      # all areas (about 1–2 minutes)
node e2e/run.mjs payments reports     # only some areas
node e2e/run.mjs --keep               # keep the databases afterwards (debugging, or for TRL_TEST_DB)
node e2e/run.mjs --teardown           # remove leftover E2E databases
```

Run from the `TRL_API` repository folder. Exit code 0 = everything passed.

## What it does

1. Safety self-check (see below), then builds `TRL_API` and `TRL_Tools` into `e2e/.out` (git-ignored), so a running
   API that locks `TRL_API/bin` doesn't matter.
2. Creates `TRL_E2E_Catalog_tmp` and provisions two clients with `TRL_Tools` (`E2EA` → `TRL_E2E_ClientA_tmp`,
   `E2EB` → `TRL_E2E_ClientB_tmp`) with a random admin password. The schema comes from `Database/01–06`, the same way a
   new client is created in production.
3. Starts the API on port 5199 with only those connections, logs in to both clients and runs the areas.
4. Stops the API and drops the three databases (unless `--keep`).

| Area | Covers |
|---|---|
| `leases` | buildings, floors, units, tenants (validation, duplicates), lease create / edit / renew / cancel renewal / end / cancel |
| `invoices` | rent generation window, re-run safety, no-lease errors, due dates, extra charges, late fee charge + reverse, cancel / reinstate, late-fee settings snapshot |
| `payments` | recording rules (balance, tenant, duplicate, all-or-nothing), discounts, waivers, adjustments, payment reversal (rules, concurrency, receipts, reports, original rows unchanged), client isolation |
| `deposits-settlement` | agreed / received / corrections / receipts, move-out settlement (dues, deductions, final payment, refunds, protections) |
| `reports` | Arrears Ageing, Rent Collection, Reminders, Collections, Tenant Statement, Billing vs Collection, Rent Roll and Dashboard cross-checked against the database |
| `auth` | login errors, deactivated / non-Admin users, cookies, refresh rotation, tampering, CSRF, logout, change password, client isolation, login rate limit (runs last) |

Results: `ok` = passed, `FAIL` = failed (exit code 1), `KNOWN` = a confirmed gap in the application that is reported
in the summary but doesn't fail the run. When one is fixed the test prints `fixed …`; then turn it into a normal check.

## Safety

The suite can only touch its own three databases:

- every database name must start with `TRL_E2E_`, end with `_tmp` **and** be one of the three names in `lib.mjs`;
  `CREATE` / `DROP` / queries refuse anything else (`SafetyError`, nothing is changed);
- `TRL_Tools` and the API read `TRL_API/appsettings.json`, which points at the real catalog and `TRL_DB`, so every
  connection is overridden by environment variables (`ConnectionStrings__Catalog`, `ClientConnections__E2EA/E2EB`,
  `ClientConnections__TRL` blanked) and every connection in that environment is checked before they start;
- a self-check runs first and stops the suite if the guards would accept `TRL_DB`, `TRL_Catalog` or a non-`_tmp` name.

## Settings

| Environment variable | Default |
|---|---|
| `TRL_E2E_SQL` | `localhost\SQLEXPRESS` (Windows authentication) |
| `TRL_E2E_PORT` | `5199` |
| `TRL_E2E_PASSWORD` | random each run (set it to log in to a `--keep` database from the browser) |

## Prerequisites and limits

- .NET 8 SDK, Node 18+, `sqlcmd` on PATH, a local SQL Server where the Windows user can create databases.
- Logins are rate limited to 10 a minute per address. A full run uses 9 and then deliberately hits the limit, so wait
  a minute before running `auth` again.
- Late fees depend on today's date: tests compare against `dbo.CalculateLateFee` for today, never fixed amounts.
- Not covered here: client deactivation and the "database needs an update" refusal (cached for 60 seconds, covered by
  unit tests), maintenance jobs, company profile, the frontend screens (see the checklist below).

## Backend database tests

`TRL_API.Tests` has database tests that run only when `TRL_TEST_DB` points at a `*_tmp` database. A kept E2E database
works:

```
node e2e/run.mjs reports --keep
set TRL_TEST_DB=Server=localhost\SQLEXPRESS;Database=TRL_E2E_ClientA_tmp;Trusted_Connection=True;TrustServerCertificate=True
dotnet test TRL_API.Tests
node e2e/run.mjs --teardown
```

## Manual browser checklist (after UI changes)

Run the frontend against an E2E API (`node e2e/run.mjs leases --keep`, start the API from `e2e/.out/api` with the same
environment, `VITE_API_BASE_URL=http://localhost:5199/api npx vite --port 3100`) or a local copy of the data:

1. Log in / log out; wrong password message; Change Password.
2. Rent Collection: record a payment with discount and waiver; receipt opens.
3. Rent History → History: Reverse a payment (reason required, late-fee warning on overdue); receipt shows REVERSED.
4. Generate rent, add an extra charge, cancel and reinstate an invoice.
5. Lease Management: create, renew, cancel renewal, end.
6. Security Deposits: record, correct; Move-out Settlement: preview, finalize, print.
7. Reports: each report loads, filters, CSV export, print preview.
