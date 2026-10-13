# Restore test for a TRL database backup (.bak): proves the file can really be restored, without touching any live
# database. It restores into a throwaway database whose name must end in _tmp, runs an integrity check (DBCC CHECKDB)
# and a few read-only checks (latest migration, row counts, money totals), then drops the throwaway database.
#
#   powershell -ExecutionPolicy Bypass -File verify-backup.ps1 -BakFile D:\Backups\db71356_2026-10-13.bak
#   options: -Server <SQL Server> (default VICTUS15\SQLEXPRESS)  -TempName <name ending in _tmp>  -Keep (don't drop)
#
# The SQL Server account must be able to read the .bak (put it in a plain folder such as D:\Backups, not under your
# user profile). A backup can only be restored on the same or a NEWER SQL Server version: production runs SQL Server
# 2025 (17.x), so restoring a production backup needs a local SQL Server 2025 (Express is free).
param(
    [Parameter(Mandatory = $true)][string]$BakFile,
    [string]$Server = 'VICTUS15\SQLEXPRESS',
    [string]$TempName = 'TRL_RestoreCheck_tmp',
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
if ($TempName -notmatch '^[A-Za-z0-9_]+_tmp$') { throw "-TempName must end in _tmp (got '$TempName'): this script only restores into throwaway databases." }
if (-not (Test-Path -LiteralPath $BakFile)) { throw "Backup file not found: $BakFile" }
$bak = (Resolve-Path -LiteralPath $BakFile).Path.Replace("'", "''")

function Q([string]$sql, [string]$db = 'master') {
    $out = sqlcmd -S $Server -E -d $db -b -I -h -1 -W -s '|' -Q "SET NOCOUNT ON; $sql" 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($out -join "`n") }
    # always a list, so [0] is the first line even when there is only one
    return , @($out | ForEach-Object { "$_".Trim() } | Where-Object { $_ -ne '' })
}

Write-Host "== Backup file: $BakFile"
if ((Q "SELECT COUNT(*) FROM sys.databases WHERE name = N'$TempName'")[0] -ne '0') { throw "Database $TempName already exists. Drop it first or pass another -TempName (ending in _tmp)." }

$header =sqlcmd -S $Server -E -b -W -s '|' -Q "SET NOCOUNT ON; RESTORE HEADERONLY FROM DISK = N'$bak'" 2>&1
if ($LASTEXITCODE -ne 0) { throw ($header -join "`n") }
$names = $header[0].Split('|'); $vals = $header[2].Split('|')
$info = @{}; for ($i = 0; $i -lt $names.Count; $i++) { $info[$names[$i].Trim()] = $vals[$i].Trim() }
$serverMajor = [int](Q "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS INT)")[0]
Write-Host ("  database={0}  finished={1}  made on SQL Server {2}.{3}  copy-only={4}" -f $info['DatabaseName'], $info['BackupFinishDate'],
    $info['SoftwareVersionMajor'], $info['SoftwareVersionMinor'], $info['IsCopyOnly'])
if ([int]$info['SoftwareVersionMajor'] -gt $serverMajor) {
    throw "This backup was made on SQL Server $($info['SoftwareVersionMajor']).x but $Server is $serverMajor.x. SQL Server can't restore a backup on an older version: use a SQL Server $($info['SoftwareVersionMajor']).x instance (e.g. SQL Server 2025 Express) or test-restore it in the hosting panel into a spare database."
}

Write-Host "== RESTORE VERIFYONLY (readable and complete)"
Q "RESTORE VERIFYONLY FROM DISK = N'$bak'" | Out-Null
Write-Host "  ok"

Write-Host "== Restore into $TempName"
$dataDir = (Q "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(400))")[0].TrimEnd('\')
$files = sqlcmd -S $Server -E -b -W -s '|' -h -1 -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$bak'" 2>&1 | Where-Object { $_ -match '\|' }
$moves = @(); $n = 0
foreach ($f in $files) { $p = $f.Split('|'); $n++; $ext = if ($p[2].Trim() -eq 'L') { 'ldf' } else { 'mdf' }; $moves += "MOVE N'$($p[0].Trim())' TO N'$dataDir\$TempName`_$n.$ext'" }
Q "RESTORE DATABASE [$TempName] FROM DISK = N'$bak' WITH $($moves -join ', '), RECOVERY" | Out-Null
Write-Host "  restored"

try {
    Write-Host "== DBCC CHECKDB (integrity)"
    Q "DBCC CHECKDB ([$TempName]) WITH NO_INFOMSGS, ALL_ERRORMSGS" | Out-Null
    Write-Host "  ok (no errors)"

    Write-Host "== Read-only checks on the restored copy"
    $r = Q ("SELECT CONCAT('latest migration=', (SELECT MAX(Name) FROM dbo.SchemaMigrations), " +
            "' | invoices=', (SELECT COUNT(*) FROM dbo.RentInvoices), ' payments=', (SELECT COUNT(*) FROM dbo.Payments), " +
            "' leases=', (SELECT COUNT(*) FROM dbo.TenantLeases), ' tenants=', (SELECT COUNT(*) FROM dbo.Tenants), " +
            "' | payment total=', (SELECT SUM(PaymentAmount) FROM dbo.Payments), ' invoice total=', (SELECT SUM(TotalRent) FROM dbo.RentInvoices), " +
            "' | users=', (SELECT COUNT(*) FROM dbo.Users))") $TempName
    Write-Host "  $($r[0])"
    Write-Host ''
    Write-Host 'RESTORE TEST PASSED: the backup restores, is consistent, and holds the data above. Compare the numbers with the live system.' -ForegroundColor Green
}
finally {
    if ($Keep) { Write-Host "Kept $TempName (drop it when done)." }
    else { Q "ALTER DATABASE [$TempName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$TempName]" | Out-Null; Write-Host "Dropped $TempName." }
}
