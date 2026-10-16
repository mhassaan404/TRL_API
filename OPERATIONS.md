# TRL operations: production settings, deployment, backups, logs and monitoring

Production today: API `https://rent-trlapi.runasp.net` (MonsterASP site95547, IIS, .NET 8), frontend
`https://voluble-lokum-rent.netlify.app` (Netlify), database `db71356` (MonsterASP SQL Server 2025, reachable only
through the MonsterASP panel / WebMSSQL). Database scripts and schema rules: [Database/README.md](Database/README.md).

## 1. Production settings (API)

Set on the host, never in `appsettings.json` or the repository. Names only; values live with the host.

| Setting | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` = `Production` | Swagger off, HSTS on |
| `JwtSettings__SecretKey` | Signing key, at least 32 bytes; changing it signs everyone out |
| `JwtSettings__Issuer`, `JwtSettings__Audience` | Optional; must stay the same once set |
| `ConnectionStrings__Catalog` | The client catalog database |
| `ClientConnections__<KEY>` (e.g. `ClientConnections__TRL`) | One per client, key = `Clients.ConnectionKey` |
| `Cors__AllowedOrigins__0` | The frontend address (`https://voluble-lokum-rent.netlify.app`) |
| `AllowedHosts` | The API host name (`appsettings.json` has `localhost`, which would refuse every other host) |
| `FileLogging__Path`, `FileLogging__KeepDays` | Optional: log folder (default `logs`) and days kept (default 30); `Path` empty = off |

The frontend calls `/api` on its own site; Netlify forwards it to the API (`public/_redirects` in the TRL repo). Build
it **without** `VITE_API_BASE_URL`. Pointing a production build straight at the API's address makes the login cookie
third-party, and browsers that block those (Safari, incognito) then show "Session expired" right after login. If the
API address ever changes (e.g. Azure), change it in `public/_redirects`.

## 2. Deployment order

1. Backup (section 3) of every client database a migration will touch.
2. Migrations: WebMSSQL versions in `D:\TRL\prod_import` (WebMSSQL runs each statement on its own, so multi-statement
   migrations are packaged as one compound statement there), one at a time, checking each result row.
3. API: Visual Studio → TRL_API → Publish → profile `site95547-WebDeploy`.
4. Frontend (only when it changed): `npm run build` (no `VITE_API_BASE_URL`) and upload the `build` folder to Netlify.
5. `powershell -ExecutionPolicy Bypass -File D:\TRL\deploy_ready\post_deploy_check.ps1` → `ALL CHECKS PASSED`.

**Phase 6 release** (packages in `D:\TRL\deploy_ready\phase6`, step by step in `DEPLOY_PHASE6.txt`):

1. Backup of db71356.
2. WebMSSQL: `precheck_10_cleanup_READONLY.sql` → no STOP rows (note the P8 totals).
3. WebMSSQL: `10W_db71356_cleanup_WEBMSSQL.sql` → result row `2026-10-13_cleanup | ok | 0 | 0 | 1 | int | …` with the P8 totals.
4. Publish `phase6\api`, upload `phase6\frontend`, run `phase6\post_deploy_check.ps1` → `ALL CHECKS PASSED`.
5. WebMSSQL: `verify_13_READONLY.sql` → W1–W6 PASS.
6. Later (after a few days of normal use): `11W_db71356_drop_tenant_unit_copy_WEBMSSQL.sql` removes the old
   unit/rent copy in `Tenants` (pending script; the Phase 6 API no longer uses it). Back up first.

The API refuses logins for a client whose database lacks `SchemaVersion.Required`, so a database must be migrated
**before** the API that needs it is published. Undo an API deploy by publishing the previous build; undo a migration
with its script in `Database/rollback/` (together with the previous API).

## 3. Backups and restore

**Make a backup** (MonsterASP panel → Databases → db71356 → Backups → **Create BAK file** → when it appears in
*Backup files*, **Download**). Store the file off the server, e.g. `D:\Backups\db71356_<yyyy-MM-dd>.bak` plus a
second copy in cloud storage. MonsterASP also keeps automatic "Safety" backups; don't rely on them alone.

| When | Keep |
|---|---|
| Before every migration, deployment or bulk change | until the change is confirmed |
| Every week (e.g. Monday) | the last 8 weekly + the last 12 monthly (first of each month) |

**Test that a backup restores** (monthly, and after changing the backup routine), without touching production:

```
powershell -ExecutionPolicy Bypass -File Database\tools\verify-backup.ps1 -BakFile D:\Backups\db71356_<date>.bak
```

It restores into `TRL_RestoreCheck_tmp` only (names must end in `_tmp`), runs `DBCC CHECKDB` and read-only checks
(latest migration, row counts, money totals), prints them and drops the copy. Compare the numbers with the live app.
A backup restores only on the same or a newer SQL Server: production is SQL Server 2025 (17.x), so this needs a local
SQL Server 2025 (Express is free). Alternative: in the panel, restore the backup into a spare, empty database (never
db71356) and run `D:\TRL\prod_import\verify_13_READONLY.sql` (`verify_12c_READONLY.sql` before Phase 6) there.

**Restore production** (only after data loss or corruption; everything recorded after the backup is lost):
1. Stop changes: tell users, or publish an `app_offline.htm` to the API site.
2. Take a backup of the current state first (even if damaged), and note payments made since the backup you will restore.
3. Panel → Databases → db71356 → Backups → restore the chosen file (orange arrow).
4. Run `verify_13_READONLY.sql` (`verify_12c_READONLY.sql` before Phase 6) in WebMSSQL and `post_deploy_check.ps1`; re-enter the missing payments.

**Local development database** (`TRL_DB`):
`sqlcmd -S VICTUS15\SQLEXPRESS -E -Q "BACKUP DATABASE TRL_DB TO DISK = N'D:\Backups\TRL_DB_<date>.bak' WITH COPY_ONLY, INIT"`.

## 4. Logs

Warnings and errors go to `logs\trl-<yyyy-MM-dd>.log` (UTC dates) in the API folder, kept 30 days: unhandled errors
(with the full exception), failed or locked logins, deactivated or non-Admin login attempts, refused refresh tokens,
failed and successful password changes, clients that are misconfigured or behind on migrations. Only client code,
username and IP are written: never passwords, tokens, request bodies or connection strings. Line breaks in logged
values are flattened, so nobody can fake a log line through a username.

A user who sees "Something went wrong … (Reference: AB12CD34)" can report the reference: search the log for it.
Read the files with the MonsterASP File Manager / WebFTP (site95547 → `logs`). IIS doesn't serve that folder: every
request goes to the API, which has no static files.

## 5. Health check and monitoring

`GET https://rent-trlapi.runasp.net/api/health` (no login): `200 {"status":"ok"}` when the catalog and every active
client database answer and are on the required schema; `503 {"status":"unavailable"}` otherwise (the reason is in the
log). The answer is reused for 30 seconds and the schema check for 60, so a problem shows within about 90 seconds.

Monitoring (free): UptimeRobot (or similar) → New monitor → HTTP(s) → the URL above, every 5 minutes, keyword `"ok"`,
alert by e-mail. It also warns when the site itself is down.

## 6. Security measures in place

Admin-only API (a test fails if an endpoint is added without `[Authorize(Roles = "Admin")]`), per-client databases
chosen only from the signed token, parameterised SQL, HttpOnly + Secure cookies, 3-minute access tokens with rotating
refresh tokens, CSRF header check on every state-changing request, CORS limited to the frontend, login limits per
address and per username, generic error messages, and security headers (API: HSTS, `nosniff`, no framing, no
referrer, `Cache-Control: no-store`; frontend via `public/_headers`: no framing, `nosniff`, referrer and permissions
policy).

Known limits: IIS still sends `Server` and `X-Powered-By` (changing that needs a `web.config` override the shared host
may not allow); the login lockout counter resets when the app restarts; the database login is the hosting owner
account (shared hosting); no two-factor login.
