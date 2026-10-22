# Prepare an already verified package; this script does not build or publish.
param([string]$PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/DBMON_SQL'))
$ErrorActionPreference = 'Stop'
$taskPackage = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$taskPackageInfo = Get-Item -LiteralPath $taskPackage
if (-not $taskPackageInfo.PSIsContainer -or $taskPackageInfo.Parent -eq $null) { throw 'Specify an application package directory, not a filesystem root.' }
$taskPrefix = $taskPackage + [IO.Path]::DirectorySeparatorChar

# Reject junctions and symbolic links before traversing or removing anything.
for ($taskAncestor = $taskPackageInfo; $null -ne $taskAncestor; $taskAncestor = $taskAncestor.Parent) {
    if ($taskAncestor.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Linked package paths are not supported: $($taskAncestor.FullName)" }
}
$taskRunning = @(Get-Process | ForEach-Object { try { if ($_.Path -and $_.Path.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase)) { $_.ProcessName } } catch { } })
if ($taskRunning.Count) { throw "The package is running. Close it before preparing distribution: $taskPackage" }

foreach ($taskFile in @('DBMON SQL.exe', 'DBMON SQL.deps.json', 'DBMON SQL.runtimeconfig.json', 'build-info.json', 'DBMON SQL.dll', 'AseIsql.Core.dll', 'AseIsql.Odbc.dll')) {
    $taskRequired = Get-Item -LiteralPath (Join-Path $taskPackage $taskFile)
    if ($taskRequired.PSIsContainer -or $taskRequired.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Invalid package file: $taskFile" }
}
$taskInfo = Get-Content -LiteralPath (Join-Path $taskPackage 'build-info.json') -Raw | ConvertFrom-Json
if ($taskInfo.coreTestsPassed -ne $true -or [int]$taskInfo.uiChecks -lt 1) { throw 'Distribution requires a package with passing core and UI checks.' }
foreach ($taskAssembly in @('DBMON SQL.dll', 'AseIsql.Core.dll', 'AseIsql.Odbc.dll')) {
    $taskActualHash = (Get-FileHash -LiteralPath (Join-Path $taskPackage $taskAssembly) -Algorithm SHA256).Hash
    if ($taskActualHash -ne $taskInfo.assemblySha256.$taskAssembly) { throw "Package differs from its verified build: $taskAssembly" }
}

$taskCleanup = @()
foreach ($taskName in @('UiSmoke', 'BobertoSmoke')) {
    $taskPath = Join-Path $taskPackage $taskName
    if (Test-Path -LiteralPath $taskPath) { $taskCleanup += Get-Item -LiteralPath $taskPath -Force }
}
$taskCleanup += @(Get-ChildItem -LiteralPath $taskPackage -File -Force | Where-Object { $_.Extension -eq '.pdb' -or $_.Name -like 'Microsoft.Web.WebView2.*.xml' })
$taskPending = New-Object 'System.Collections.Generic.Stack[System.IO.FileSystemInfo]'
foreach ($taskItem in $taskCleanup) { $taskPending.Push($taskItem) }
while ($taskPending.Count) {
    $taskItem = $taskPending.Pop()
    $taskTarget = [IO.Path]::GetFullPath($taskItem.FullName)
    if (-not $taskTarget.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase) -or $taskItem.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Unsafe cleanup target: $taskTarget" }
    if ($taskItem.PSIsContainer) {
        foreach ($taskChild in @(Get-ChildItem -LiteralPath $taskTarget -Force)) { $taskPending.Push($taskChild) }
    }
}
foreach ($taskName in @('README.txt', 'LICENSE.txt')) {
    $taskPath = Join-Path $taskPackage $taskName
    if (Test-Path -LiteralPath $taskPath) {
        $taskItem = Get-Item -LiteralPath $taskPath -Force
        if ($taskItem.PSIsContainer -or $taskItem.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Unsafe distribution document: $taskPath" }
    }
}

$taskInstructions = @"
DBMON SQL $($taskInfo.version) ($($taskInfo.kind) build)

Extract the entire package to a local folder and run DBMON SQL.exe.
Keep all DLLs, JSON files and subfolders together. No separate .NET install is needed.

Requirements:
- Windows 10 or 11, 64-bit.
- SAP ASE ODBC driver, 64-bit, for SAP ASE and Replication Server.
- SAP IQ ODBC driver, 64-bit, for SAP IQ only.
- Server network access and your own login credentials.
- Your own sql.ini with server aliases, or enter host and port manually.
- Microsoft Edge WebView2 Runtime for Boberto. The SQL editor works without it.

Profiles and preferences are stored per Windows user in %LOCALAPPDATA%\DBMON_SQL.
Do not distribute that personal data folder or saved credentials.
build-info.json identifies this build and its recorded checks.
Development builds are for testing and are not formal releases.

Source and license: https://github.com/ChristianBuzzetti/DBMON_SQL
"@
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../LICENSE') -Destination (Join-Path $taskPackage 'LICENSE.txt')
Set-Content -LiteralPath (Join-Path $taskPackage 'README.txt') -Value $taskInstructions -Encoding UTF8
foreach ($taskItem in $taskCleanup) { Remove-Item -LiteralPath $taskItem.FullName -Recurse -Force }
Write-Output "Distribution ready: $taskPackage"
