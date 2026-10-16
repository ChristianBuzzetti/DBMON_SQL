# Records the README tour: runs DBMON SQL's synthetic showcase (--smoke --showcase), which composes frames from its
# own windows only (PrintWindow), then encodes an MP4 and a GIF and copies the scene stills to docs/media.
# Needs a Release build (./build.ps1) and ffmpeg on PATH. The pointer moves on its own for about 45 seconds.
param([string]$Output = 'docs/media', [int]$Fps = 15)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src/AseIsql.Desktop/bin/Release/net10.0-windows/DBMON SQL.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build first: ./build.ps1' }
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'ffmpeg is required on PATH.' }
$localDotnet = Join-Path $root '.tools/dotnet'
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = $localDotnet }
$work = Join-Path ([IO.Path]::GetTempPath()) ('dbmon-showcase-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$out = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
New-Item -ItemType Directory -Force -Path $out | Out-Null
try {
    $app = Start-Process -FilePath $exe -ArgumentList ('--smoke --showcase "' + $work + '"') -PassThru
    if (-not $app.WaitForExit(240000)) { $app.Kill(); throw 'The showcase did not finish within 4 minutes.' }
    if ($app.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $work 'done.txt'))) { throw "The showcase failed (exit $($app.ExitCode))." }
    $frames = Join-Path $work 'frames/frame_%05d.png'
    $video = Join-Path $out 'dbmon-sql-tour.mp4'; $gif = Join-Path $out 'dbmon-sql-tour.gif'; $palette = Join-Path $work 'frames/palette.png'
    & ffmpeg -y -loglevel error -framerate $Fps -i $frames -vf 'scale=1280:-2:flags=lanczos' -c:v libx264 -preset slow -crf 21 -pix_fmt yuv420p -movflags +faststart $video
    # Two-pass palette keeps the GIF crisp and small enough for a README.
    & ffmpeg -y -loglevel error -framerate $Fps -i $frames -vf 'fps=12,scale=960:-1:flags=lanczos,palettegen=max_colors=160:stats_mode=diff' $palette
    & ffmpeg -y -loglevel error -framerate $Fps -i $frames -i $palette -lavfi 'fps=12,scale=960:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle' $gif
    foreach ($still in Get-ChildItem -LiteralPath $work -Filter '*.png') { Copy-Item -LiteralPath $still.FullName -Destination (Join-Path $out $still.Name) -Force }
    Get-ChildItem -LiteralPath $out | Select-Object Name, @{n = 'KB'; e = { [int]($_.Length / 1KB) } }
} finally { if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force } }
