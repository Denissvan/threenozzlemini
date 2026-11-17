param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
# Only parse strings with the deployed SQLite assembly; never open a database.
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $RepoRoot 'UI/Class/SQLData.cs')
$helper = [regex]::Match($source, '(?s)private static string BuildSQLiteDataSource\(string filename\).*?\r?\n        }')
if ($helper.Success) {
    $method = $helper.Value.Replace('private static', 'public static')
    if ([regex]::Matches($source, 'return BuildSQLiteDataSource\(filename\);').Count -ne 5) {
        throw 'All five database path factories must use the shared formatter.'
    }
} else {
    # Exercise the existing formatter before the fix as well.
    $oldReturn = [regex]::Match($source, 'return string.Format\("data source=\{0\}", filename\);')
    if (!$oldReturn.Success) { throw 'Database formatter not found.' }
    $method = 'public static string BuildSQLiteDataSource(string filename) { ' + $oldReturn.Value + ' }'
}
Add-Type -TypeDefinition ('public static class SQLitePathHarness { ' + $method + ' }')
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $RepoRoot 'UI/bin/x64/System.Data.SQLite.dll'))
$type = $assembly.GetType('System.Data.SQLite.SQLiteConnection')
$parser = $type.GetMethods([Reflection.BindingFlags]'Static,NonPublic,Public') |
    Where-Object { $_.Name -eq 'ParseConnectionString' -and $_.GetParameters().Count -eq 2 }
if (!$parser) { throw 'SQLite connection string parser not found.' }
$checks = 0
foreach ($root in @('D:\Machine\bin', '\\192.168.0.50\bin', '\\machine-name\machine share')) {
    foreach ($folder in @('G1A001B-20C-Z-M\DataBase', 'TestcloseDataBase', 'TestimeDataBase', 'G1A001B-20C-Z-M\AlarmDataBase', 'G1A001B-20C-Z-M\SysTimeBase')) {
        $path = $root + '\product\' + $folder + '\2026_10.db'
        $connection = [SQLitePathHarness]::BuildSQLiteDataSource($path)
        $parsed = $parser.Invoke($null, [object[]]@($connection, $false))
        if ($parsed['data source'] -cne $path) {
            throw "SQLite changed the database path. Expected: $path; parsed: $($parsed['data source'])"
        }
        $checks++
    }
}
Write-Output "PASS: $checks SQLite path round trips using $($assembly.GetName().Version); no database opened."
