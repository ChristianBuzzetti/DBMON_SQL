param([switch]$Test, [switch]$Publish, [switch]$Release, [string]$PublishDirectory = '')
$ErrorActionPreference = 'Stop'
if ($Publish -and -not $Test) { throw 'Publishing requires -Test so retention only follows a verified release.' }
if ($Release -and -not $Publish) { throw 'A formal release requires -Test -Publish -Release.' }
[xml]$taskBuildProperties = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw
$taskVersion = [version]$taskBuildProperties.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = if($Release) { 'artifacts/windows-x64-v' + $taskVersion.ToString(3) + '-final' } else { 'artifacts/DBMON_SQL' }
}
if ($Publish) {
    $taskOutputPath = if([IO.Path]::IsPathRooted($PublishDirectory)) { [IO.Path]::GetFullPath($PublishDirectory) } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PublishDirectory)) }
    $taskPublishPrefix = $taskOutputPath + [IO.Path]::DirectorySeparatorChar
    $taskOpenRelease = @(Get-Process | ForEach-Object { try { if ($_.Path -and $_.Path.StartsWith($taskPublishPrefix,[StringComparison]::OrdinalIgnoreCase)) { $_ } } catch { } })
    if ($taskOpenRelease.Count) { throw "The target build is running. Close it or choose another output directory without changing the product version: $taskOutputPath" }
}
$taskDotnet = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
Push-Location $PSScriptRoot
try {
    & $taskDotnet build src/AseIsql.Desktop/AseIsql.Desktop.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Test) {
        & $taskDotnet run --project tests/AseIsql.Checks/AseIsql.Checks.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    }
    if ($Publish) {
        # The product was renamed to DBMON SQL; files of the former executable name must not linger in the package.
        if (Test-Path -LiteralPath $taskOutputPath) { Get-ChildItem -LiteralPath $taskOutputPath -File -Filter 'ASE ISQL Desktop.*' | ForEach-Object { [IO.File]::Delete($_.FullName) } }
        & $taskDotnet publish src/AseIsql.Desktop/AseIsql.Desktop.csproj -c Release -r win-x64 --self-contained true -o $taskOutputPath --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
        $taskReport = Join-Path $PSScriptRoot 'artifacts/smoke-latest.json'
        if(Test-Path -LiteralPath $taskReport) { Remove-Item -LiteralPath $taskReport }
        $taskSmoke = Start-Process -FilePath (Join-Path $taskOutputPath 'DBMON SQL.exe') -ArgumentList ('--smoke --report "' + $taskReport + '"') -WindowStyle Hidden -PassThru
        if (-not $taskSmoke.WaitForExit(120000)) { throw 'UI verification did not finish within 120 seconds; no releases were removed.' }
        $taskSmokeResult = Get-Content -LiteralPath $taskReport -Raw | ConvertFrom-Json
        if ($taskSmoke.ExitCode -ne 0 -or $taskSmokeResult.exitCode -ne 0) { throw 'UI verification failed; no releases were removed. See artifacts/smoke-latest.json.' }
        Write-Output ("UI verification passed: " + $taskSmokeResult.checks.Count + " checks and measurements.")
        $taskAssemblyHashes = @{}
        foreach($taskAssembly in @('DBMON SQL.dll','AseIsql.Core.dll','AseIsql.Odbc.dll')) { $taskAssemblyHashes[$taskAssembly] = (Get-FileHash -LiteralPath (Join-Path $taskOutputPath $taskAssembly) -Algorithm SHA256).Hash }
        [ordered]@{version=$taskVersion.ToString(3);builtAtUtc=[DateTime]::UtcNow.ToString('O');kind=$(if($Release){'release'}else{'development'});coreTestsPassed=$true;uiChecks=$taskSmokeResult.checks.Count;assemblySha256=$taskAssemblyHashes} | ConvertTo-Json -Depth 4 | Set-Variable taskBuildInfo
        # The smoke's WebView2 processes can hold the folder for a moment after exit; retry the marker briefly.
        for($taskTry=1;;$taskTry++) { try { Set-Content -LiteralPath (Join-Path $taskOutputPath 'build-info.json') -Value $taskBuildInfo -Encoding utf8 -ErrorAction Stop; break } catch [System.IO.IOException] { if($taskTry -ge 10) { throw }; Start-Sleep -Milliseconds 500 } }
        & (Join-Path $PSScriptRoot 'scripts/prepare-distribution.ps1') -PackageDirectory $taskOutputPath
        if ($Release -and (Split-Path $taskOutputPath -Leaf) -match '^windows-x64-v\d+\.\d+\.\d+-final$') { & (Join-Path $PSScriptRoot 'scripts/prune-releases.ps1') -CurrentRelease $taskOutputPath }
    }
} finally { Pop-Location }
