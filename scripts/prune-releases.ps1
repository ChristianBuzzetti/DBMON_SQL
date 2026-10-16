param([switch]$DryRun,[string]$CurrentRelease='')
$ErrorActionPreference = 'Stop'
$taskArtifactRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts'))
$taskRootPrefix = $taskArtifactRoot + [IO.Path]::DirectorySeparatorChar
if([string]::IsNullOrWhiteSpace($CurrentRelease)) { throw 'Specify -CurrentRelease for a verified formal release. Development builds are not pruned.' }
$taskCurrent = [IO.Path]::GetFullPath($CurrentRelease)
if(-not $taskCurrent.StartsWith($taskRootPrefix,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $taskCurrent -Leaf) -notmatch '^windows-x64-v\d+\.\d+\.\d+-final$') { throw 'The current release must be a named final package inside artifacts.' }
$taskReleases = @(Get-ChildItem -LiteralPath $taskArtifactRoot -Directory | Where-Object { $_.Name -match '^windows-x64-v\d+\.\d+\.\d+-final$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'ASE ISQL Desktop.exe')) } | Sort-Object { [version]($_.Name -replace '^windows-x64-v','' -replace '-final$','') } -Descending)
if ($taskReleases.Count -lt 2) { throw 'Retention requires two verified final releases before cleanup.' }
if(-not ($taskReleases | Where-Object {$_.FullName -eq $taskCurrent})) { throw 'The current release package does not exist.' }
$taskPrevious = $taskReleases | Where-Object {$_.FullName -ne $taskCurrent} | Select-Object -First 1
$taskKeep = @($taskCurrent,$taskPrevious.FullName)
$taskCandidates = @(Get-ChildItem -LiteralPath $taskArtifactRoot -Directory | Where-Object { $_.Name -match '^windows-x64(?:-v\d+\.\d+(?:\.\d+)?(?:-final)?)?$' -and $_.FullName -notin $taskKeep })
$taskRunningPaths = @(Get-Process | ForEach-Object { try { $_.Path } catch { } } | Where-Object { $_ })
foreach ($taskCandidate in $taskCandidates) {
    $taskTarget = [IO.Path]::GetFullPath($taskCandidate.FullName)
    if (-not $taskTarget.StartsWith($taskRootPrefix,[StringComparison]::OrdinalIgnoreCase) -or $taskTarget -eq $taskArtifactRoot -or $taskCandidate.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Unsafe cleanup target: $taskTarget" }
    if ($taskRunningPaths | Where-Object { $_.StartsWith($taskTarget + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) }) { throw "Release is still running; preserve the session and close it before cleanup: $taskTarget" }
}
foreach ($taskCandidate in $taskCandidates) {
    if ($DryRun) { Write-Output "Would remove: $($taskCandidate.FullName)" }
    else { Remove-Item -LiteralPath $taskCandidate.FullName -Recurse -Force; Write-Output "Removed: $($taskCandidate.Name)" }
}
Write-Output ('Retained: ' + (($taskKeep | ForEach-Object {Split-Path $_ -Leaf}) -join ', '))
