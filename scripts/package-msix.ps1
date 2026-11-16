# Packs an already verified distribution (build.ps1 -Test -Publish) into an MSIX for Microsoft Store.
# The package directory is copied, never changed. Output: artifacts\store\DBMON_SQL_<version>.0_x64.msix (unsigned:
# the Store signs it). -TestSign also writes a ..._test.msix signed with a self-signed certificate of the current user
# (CN = Publisher in packaging\store.json) and exports the .cer, to install it on this PC before uploading.
param([string]$PackageDirectory = 'C:\DBMON_SQL', [switch]$TestSign)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$package = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$info = Get-Content -LiteralPath (Join-Path $package 'build-info.json') -Raw | ConvertFrom-Json
if ($info.coreTestsPassed -ne $true -or [int]$info.uiChecks -lt 1) { throw 'The Store package requires a distribution with passing core and UI checks.' }
foreach ($assembly in @('DBMON SQL.dll', 'AseIsql.Core.dll', 'AseIsql.Odbc.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $package $assembly) -Algorithm SHA256).Hash -ne $info.assemblySha256.$assembly) { throw "Package differs from its verified build: $assembly" }
}
foreach ($leftover in @('UiSmoke', 'BobertoSmoke')) { if (Test-Path -LiteralPath (Join-Path $package $leftover)) { throw "Smoke files found in the package: $leftover. Run build.ps1 -Test -Publish." } }
$msixVersion = "$($info.version).0"                    # the Store needs four parts, the last one 0
$identity = Get-Content -LiteralPath (Join-Path $root 'packaging\store.json') -Raw | ConvertFrom-Json
$store = Join-Path $root 'artifacts\store'
$cache = Join-Path $root 'artifacts\cache'
$stage = Join-Path $store 'app'
New-Item -ItemType Directory -Force $store, $cache | Out-Null
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
Copy-Item -LiteralPath $package -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $stage

# Windows SDK tools (makeappx, makepri, signtool) from Microsoft's official NuGet package.
$btVersion = '10.0.28000.2957'
$bt = Join-Path $cache "sdk-buildtools-$btVersion"
if (-not (Test-Path -LiteralPath (Join-Path $bt 'bin'))) {
    $nupkg = Join-Path $cache "sdk-buildtools-$btVersion.zip"
    Invoke-WebRequest -UseBasicParsing "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$btVersion/microsoft.windows.sdk.buildtools.$btVersion.nupkg" -OutFile $nupkg
    Expand-Archive -LiteralPath $nupkg -DestinationPath $bt -Force
    Remove-Item -LiteralPath $nupkg
}
$sdkBin = Get-ChildItem -LiteralPath (Join-Path $bt 'bin') -Directory | Sort-Object Name | Select-Object -Last 1 | ForEach-Object { Join-Path $_.FullName 'x64' }
$makeappx = Join-Path $sdkBin 'makeappx.exe'; $makepri = Join-Path $sdkBin 'makepri.exe'; $signtool = Join-Path $sdkBin 'signtool.exe'

# Logos from the shipped mark (teal tile; IconMaker writes dbmon-mark.png).
Add-Type -AssemblyName System.Drawing
$images = Join-Path $stage 'Images'
New-Item -ItemType Directory -Force $images | Out-Null
$src = [Drawing.Image]::FromFile((Join-Path $root 'src\AseIsql.Desktop\Assets\dbmon-mark.png'))
function Save-Logo([string]$name, [int]$w, [int]$h, [double]$fill) {
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'
    $s = [int]([Math]::Min($w, $h) * $fill)
    $g.DrawImage($src, [int](($w - $s) / 2), [int](($h - $s) / 2), $s, $s)
    $g.Dispose(); $bmp.Save((Join-Path $images $name), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
try {
    foreach ($sc in 100, 200) {
        $f = $sc / 100
        Save-Logo "Square44x44Logo.scale-$sc.png" (44 * $f) (44 * $f) 1
        Save-Logo "Square150x150Logo.scale-$sc.png" (150 * $f) (150 * $f) 0.6
        Save-Logo "Wide310x150Logo.scale-$sc.png" (310 * $f) (150 * $f) 0.6
        Save-Logo "StoreLogo.scale-$sc.png" (50 * $f) (50 * $f) 1
    }
    foreach ($t in 16, 24, 32, 48, 256) {
        Save-Logo "Square44x44Logo.targetsize-$t.png" $t $t 1
        Save-Logo "Square44x44Logo.targetsize-${t}_altform-unplated.png" $t $t 1
    }
} finally { $src.Dispose() }
$manifest = (Get-Content -LiteralPath (Join-Path $root 'packaging\AppxManifest.xml') -Raw).
    Replace('{{IdentityName}}', $identity.IdentityName).Replace('{{Publisher}}', [Security.SecurityElement]::Escape($identity.Publisher)).
    Replace('{{PublisherDisplayName}}', [Security.SecurityElement]::Escape($identity.PublisherDisplayName)).Replace('{{Version}}', $msixVersion)
[IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

# resources.pri (so Windows picks the scale and the unplated taskbar icon), then the package.
$priConfig = Join-Path $store 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed.' }
& $makepri new /pr $stage /cf $priConfig /mn (Join-Path $stage 'AppxManifest.xml') /of (Join-Path $stage 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri new failed.' }
$msixPath = Join-Path $store "DBMON_SQL_${msixVersion}_x64.msix"
& $makeappx pack /d $stage /p $msixPath /o /nv | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed.' }
Write-Output ("MSIX (upload to the Store): $msixPath ({0:N0} MB)" -f ((Get-Item -LiteralPath $msixPath).Length / 1MB))

if ($TestSign) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $identity.Publisher -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $identity.Publisher -KeyUsage DigitalSignature -FriendlyName 'DBMON SQL MSIX test' `
            -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    $testPath = Join-Path $store "DBMON_SQL_${msixVersion}_x64_test.msix"
    Copy-Item -LiteralPath $msixPath $testPath -Force
    & $signtool sign /q /fd SHA256 /sha1 $cert.Thumbprint /s My $testPath
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed.' }
    $cer = Join-Path $store 'DBMON_SQL-test.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Write-Output "Test-signed MSIX: $testPath"
    Write-Output "To install it, once and as administrator: Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
}
