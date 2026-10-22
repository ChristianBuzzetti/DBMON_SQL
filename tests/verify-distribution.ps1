$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path $PSScriptRoot -Parent
$taskScript = Join-Path $taskRepository 'scripts/prepare-distribution.ps1'
$taskScratch = Join-Path ([IO.Path]::GetTempPath()) ('dbmon-distribution-' + [guid]::NewGuid().ToString('N'))
$taskChecks = 0

function Assert-Distribution($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:taskChecks++
}

function New-Package([string]$Name) {
    $taskPath = Join-Path $taskScratch $Name
    New-Item -ItemType Directory -Path $taskPath -Force | Out-Null
    $taskHashes = @{}
    foreach ($taskFile in @('DBMON SQL.exe', 'DBMON SQL.dll', 'AseIsql.Core.dll', 'AseIsql.Odbc.dll', 'DBMON SQL.deps.json', 'DBMON SQL.runtimeconfig.json')) {
        Set-Content -LiteralPath (Join-Path $taskPath $taskFile) -Value 'fixture'
        if ($taskFile -like '*.dll') { $taskHashes[$taskFile] = (Get-FileHash -LiteralPath (Join-Path $taskPath $taskFile)).Hash }
    }
    @{ version = '0.1.0'; kind = 'development'; coreTestsPassed = $true; uiChecks = 326; assemblySha256 = $taskHashes } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskPath 'build-info.json')
    foreach ($taskFile in @('UiSmoke/preview.png', 'BobertoSmoke/profile/cache.dat', 'DBMON SQL.pdb', 'Microsoft.Web.WebView2.Core.xml', 'Assets/Boberto/index.html', 'Assets/Fonts/OFL.txt', 'runtimes/win-x64/native/WebView2Loader.dll', 'es/PresentationFramework.resources.dll')) {
        $taskFullName = Join-Path $taskPath $taskFile
        New-Item -ItemType Directory -Path (Split-Path $taskFullName -Parent) -Force | Out-Null
        Set-Content -LiteralPath $taskFullName -Value 'fixture'
    }
    return $taskPath
}

function Get-Snapshot([string]$Path) {
    return (@(Get-ChildItem -LiteralPath $Path -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($Path.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash
    }) -join "`n")
}

try {
    $taskPackage = New-Package 'valid'
    $taskRetained = @('DBMON SQL.exe', 'DBMON SQL.dll', 'AseIsql.Core.dll', 'AseIsql.Odbc.dll', 'DBMON SQL.deps.json', 'DBMON SQL.runtimeconfig.json', 'build-info.json', 'Assets/Boberto/index.html', 'Assets/Fonts/OFL.txt', 'runtimes/win-x64/native/WebView2Loader.dll', 'es/PresentationFramework.resources.dll')
    $taskBefore = @{}
    foreach ($taskFile in $taskRetained) { $taskBefore[$taskFile] = (Get-FileHash -LiteralPath (Join-Path $taskPackage $taskFile)).Hash }
    & $taskScript -PackageDirectory $taskPackage
    foreach ($taskFile in $taskRetained) {
        Assert-Distribution ((Get-FileHash -LiteralPath (Join-Path $taskPackage $taskFile)).Hash -eq $taskBefore[$taskFile]) "Required package file changed: $taskFile"
    }
    foreach ($taskFile in @('UiSmoke', 'BobertoSmoke', 'DBMON SQL.pdb', 'Microsoft.Web.WebView2.Core.xml')) {
        Assert-Distribution (-not (Test-Path -LiteralPath (Join-Path $taskPackage $taskFile))) "Unnecessary output remains: $taskFile"
    }
    Assert-Distribution ((Get-FileHash -LiteralPath (Join-Path $taskPackage 'LICENSE.txt')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $taskRepository 'LICENSE')).Hash) 'MIT license missing or changed.'
    Assert-Distribution ((Get-Content -LiteralPath (Join-Path $taskPackage 'README.txt') -Raw) -match 'SAP.*ODBC') 'Recipient requirements missing.'
    $taskSnapshot = Get-Snapshot $taskPackage
    & $taskScript -PackageDirectory $taskPackage
    Assert-Distribution ((Get-Snapshot $taskPackage) -eq $taskSnapshot) 'Repeated preparation must be idempotent.'

    # The default command must prepare the canonical package without a path override.
    $taskDefaultPackage = New-Package 'default-layout/artifacts/DBMON_SQL'
    $taskDefaultRoot = Join-Path $taskScratch 'default-layout'
    $taskDefaultScripts = Join-Path $taskDefaultRoot 'scripts'
    New-Item -ItemType Directory -Path $taskDefaultScripts | Out-Null
    Copy-Item -LiteralPath $taskScript -Destination $taskDefaultScripts
    Copy-Item -LiteralPath (Join-Path $taskRepository 'LICENSE') -Destination $taskDefaultRoot
    & (Join-Path $taskDefaultScripts 'prepare-distribution.ps1')
    Assert-Distribution (Test-Path -LiteralPath (Join-Path $taskDefaultPackage 'README.txt')) 'The default command must prepare artifacts/DBMON_SQL.'
    Assert-Distribution (-not (Test-Path -LiteralPath (Join-Path $taskDefaultPackage 'UiSmoke'))) 'The default package must be cleaned.'
    Assert-Distribution (-not (Test-Path -LiteralPath (Join-Path $taskDefaultRoot 'artifacts/windows-x64-dev'))) 'Preparation must not create a second development package.'

    foreach ($taskInvalid in @('tampered', 'unverified', 'missing-runtime')) {
        $taskPackage = New-Package $taskInvalid
        switch ($taskInvalid) {
            'tampered' { Add-Content -LiteralPath (Join-Path $taskPackage 'DBMON SQL.dll') -Value 'modified' }
            'unverified' {
                $taskInfo = Get-Content -LiteralPath (Join-Path $taskPackage 'build-info.json') -Raw | ConvertFrom-Json
                $taskInfo.coreTestsPassed = $false
                $taskInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskPackage 'build-info.json')
            }
            'missing-runtime' { Remove-Item -LiteralPath (Join-Path $taskPackage 'DBMON SQL.runtimeconfig.json') }
        }
        $taskSnapshot = Get-Snapshot $taskPackage
        $taskRejected = $false
        try { & $taskScript -PackageDirectory $taskPackage } catch { $taskRejected = $true }
        Assert-Distribution $taskRejected "Invalid package accepted: $taskInvalid"
        Assert-Distribution ((Get-Snapshot $taskPackage) -eq $taskSnapshot) "Rejected package was modified: $taskInvalid"
    }
    Write-Output "Distribution checks passed: $taskChecks."
} finally {
    $taskResolvedScratch = [IO.Path]::GetFullPath($taskScratch)
    $taskTempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $taskResolvedScratch.StartsWith($taskTempPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $taskResolvedScratch -Leaf) -notlike 'dbmon-distribution-*') { throw 'Unsafe scratch cleanup target.' }
    if (Test-Path -LiteralPath $taskResolvedScratch) { Remove-Item -LiteralPath $taskResolvedScratch -Recurse -Force }
}
