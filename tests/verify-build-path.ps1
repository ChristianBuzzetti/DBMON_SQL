$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'build.ps1'))
$taskAst = [Management.Automation.Language.Parser]::ParseInput($taskSource, [ref]$null, [ref]$null)
# Exercise the real parameter binding and path guards, stopping before any build or publication.
$taskToolAssignment = $taskAst.EndBlock.Statements | Where-Object {
    $_ -is [Management.Automation.Language.AssignmentStatementAst] -and $_.Left.VariablePath.UserPath -eq 'taskDotnet'
} | Select-Object -First 1
if (-not $taskToolAssignment) { throw 'Build preamble boundary not found.' }
$taskPreamble = $taskSource.Substring(0, $taskToolAssignment.Extent.StartOffset)
$taskParamEnd = $taskAst.ParamBlock.Extent.EndOffset
$taskScratch = Join-Path ([IO.Path]::GetTempPath()) ('dbmon-build-path-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $taskScratch | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Destination $taskScratch
    $taskProbe = Join-Path $taskScratch 'probe.ps1'
    function Write-Probe([string]$ProcessPath = '') {
        $taskMock = if ($ProcessPath) { "function Get-Process { [pscustomobject]@{Path='$ProcessPath'} }" } else { 'function Get-Process { @() }' }
        $taskBody = $taskPreamble.Insert($taskParamEnd, "`n$taskMock`n") + "`nWrite-Output `$PublishDirectory`n"
        Set-Content -LiteralPath $taskProbe -Value $taskBody -Encoding utf8
    }
    Write-Probe
    $taskDefault = & $taskProbe -Test -Publish
    if ($taskDefault -ne 'C:\DBMON_SQL') { throw "Expected C:\DBMON_SQL, received $taskDefault" }
    $taskRelease = & $taskProbe -Test -Publish -Release
    if ($taskRelease -ne 'artifacts/windows-x64-v0.1.0-final') { throw 'Formal release destination changed.' }
    $taskOverride = & $taskProbe -Test -Publish -PublishDirectory 'artifacts/custom-package'
    if ($taskOverride -ne 'artifacts/custom-package') { throw 'Explicit destination override changed.' }
    Write-Probe 'C:\DBMON_SQL\DBMON SQL.exe'
    $taskRejected = $false
    try { & $taskProbe -Test -Publish | Out-Null } catch { $taskRejected = $_.Exception.Message -match 'running' }
    if (-not $taskRejected) { throw 'An application running in the output directory must prevent publication.' }
    Write-Probe 'C:\DBMON_SQL-other\DBMON SQL.exe'
    if ((& $taskProbe -Test -Publish) -ne 'C:\DBMON_SQL') { throw 'An unrelated directory prevented publication.' }
    Write-Output 'Build destination checks passed: 5. No application was built or launched.'
} finally {
    $taskScratchFull = [IO.Path]::GetFullPath($taskScratch)
    $taskTempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $taskScratchFull.StartsWith($taskTempPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $taskScratchFull -Leaf) -notlike 'dbmon-build-path-*') { throw 'Unsafe scratch cleanup target.' }
    if (Test-Path -LiteralPath $taskScratchFull) { Remove-Item -LiteralPath $taskScratchFull -Recurse -Force }
}
