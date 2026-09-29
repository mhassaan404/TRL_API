param([string]$OutDir)
$ide = "C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE"
Add-Type -TypeDefinition @"
using System; using System.IO; using System.Reflection; using System.Collections.Generic;
public static class SmoResolver {
    static Dictionary<string,string> idx = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); static HashSet<string> busy = new HashSet<string>();
    public static void Hook(string d) { foreach (var f in Directory.GetFiles(d, "*.dll", SearchOption.AllDirectories)) { var k = Path.GetFileNameWithoutExtension(f); if (!idx.ContainsKey(k)) idx[k] = f; } AppDomain.CurrentDomain.AssemblyResolve += Resolve; }
    static Assembly Resolve(object s, ResolveEventArgs e) {
        string n = new AssemblyName(e.Name).Name;
        if (n.EndsWith(".resources") || busy.Contains(n)) return null;
        string p; if (!idx.TryGetValue(n, out p)) return null;

        busy.Add(n); try { return Assembly.LoadFrom(p); } finally { busy.Remove(n); }
    }
}
"@
[SmoResolver]::Hook($ide)
foreach ($n in 'Microsoft.SqlServer.Management.Sdk.Sfc','Microsoft.SqlServer.ConnectionInfo','Microsoft.SqlServer.Smo','Microsoft.SqlServer.SqlEnum') { [void][Reflection.Assembly]::LoadFrom((Join-Path $ide "$n.dll")) }

$server = New-Object Microsoft.SqlServer.Management.Smo.Server 'VICTUS15\SQLEXPRESS'
$server.ConnectionContext.TrustServerCertificate = $true
$server.ConnectionContext.StatementTimeout = 60
$db = $server.Databases['TRL_DB']

$opt = New-Object Microsoft.SqlServer.Management.Smo.ScriptingOptions
$opt.SchemaQualify = $true
$opt.IncludeHeaders = $false
$opt.DriAll = $false; $opt.DriPrimaryKey = $true; $opt.DriUniqueKeys = $true; $opt.DriChecks = $true; $opt.DriDefaults = $true; $opt.DriIndexes = $true; $opt.DriClustered = $true; $opt.DriNonClustered = $true
$opt.Indexes = $true
$opt.Triggers = $true
$opt.Default = $true
$opt.ExtendedProperties = $true
$opt.ScriptBatchTerminator = $true
$opt.NoCollation = $true
$opt.IncludeIfNotExists = $false
$opt.WithDependencies = $false

$scripter = New-Object Microsoft.SqlServer.Management.Smo.Scripter $server
$scripter.Options = $opt

$tables = @($db.Tables | Where-Object { -not $_.IsSystemObject })
$views = @($db.Views | Where-Object { -not $_.IsSystemObject })
$funcs = @($db.UserDefinedFunctions | Where-Object { -not $_.IsSystemObject })
$procs = @($db.StoredProcedures | Where-Object { -not $_.IsSystemObject })

# Tables are scripted without foreign keys; FKs go in a later file so creation order doesn't matter.
$opt.DriForeignKeys = $false

function Emit($objs, $file, $title, $o2 = $opt) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("-- TRL_DB: $title")
    [void]$sb.AppendLine("-- Generated from VICTUS15\SQLEXPRESS (SQL Server 2016). Schema only, no data.")
    [void]$sb.AppendLine("")
    foreach ($o in $objs) {
        foreach ($line in $o.Script($o2)) {
            [void]$sb.AppendLine($line.TrimEnd())
            [void]$sb.AppendLine("GO")
        }
        [void]$sb.AppendLine("")
    }
    [IO.File]::WriteAllText((Join-Path $OutDir $file), $sb.ToString(), (New-Object Text.UTF8Encoding $false))
    "{0}: {1} objects" -f $file, @($objs).Count
}

$tables = @($tables | Sort-Object Name)
$fks = @($tables | ForEach-Object { $_.ForeignKeys } )
Emit $tables '01_tables.sql' 'tables, primary keys, indexes, defaults, checks'
Emit $funcs '02_functions.sql' 'user-defined functions'
Emit $views '03_views.sql' 'views'
$fkOpt = New-Object Microsoft.SqlServer.Management.Smo.ScriptingOptions; $fkOpt.SchemaQualify = $true; $fkOpt.DriForeignKeys = $true
Emit $fks '04_foreign_keys.sql' 'foreign keys' $fkOpt
if ($procs.Count) { Emit $procs '05_procedures.sql' 'stored procedures' }
"tables: " + (($tables | ForEach-Object { $_.Name }) -join ', ')
"functions: " + (($funcs | ForEach-Object { $_.Name }) -join ', ')
"views: " + (($views | ForEach-Object { $_.Name }) -join ', ')
"procs: " + (($procs | ForEach-Object { $_.Name }) -join ', ')
