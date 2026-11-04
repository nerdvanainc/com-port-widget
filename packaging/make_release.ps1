<#
Builds the release zip and SHA256SUMS.txt under dist/.

  pwsh packaging/make_release.ps1 -Version 0.1.0

Output:
  dist/ComPortWidget-<Version>-win64.zip
  dist/SHA256SUMS.txt
The zip holds ComPortWidget.exe, LICENSE, NOTICE.md, NOTICE.en.md and THIRD_PARTY_NOTICES.md.
dist/ is not tracked by git.
#>
param(
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3: $Version" }

$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$publish = Join-Path $dist 'publish'
$stage = Join-Path $dist 'stage'
$zipName = "ComPortWidget-$Version-win64.zip"
$zipPath = Join-Path $dist $zipName

foreach ($d in @($publish, $stage)) {
    if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
}
foreach ($f in @($zipPath, (Join-Path $dist 'SHA256SUMS.txt'))) {
    if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
}
New-Item -ItemType Directory -Force -Path $publish, $stage | Out-Null

Write-Host "Publishing version $Version ..."
& dotnet publish (Join-Path $repo 'src/ComPortWidget.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $publish 'ComPortWidget.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "ComPortWidget.exe was not produced." }

Copy-Item -LiteralPath $exe -Destination $stage
foreach ($name in @('LICENSE', 'NOTICE.md', 'NOTICE.en.md', 'THIRD_PARTY_NOTICES.md')) {
    $src = Join-Path $repo $name
    if (-not (Test-Path -LiteralPath $src)) { throw "Missing file: $name" }
    Copy-Item -LiteralPath $src -Destination $stage
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$sumsPath = Join-Path $dist 'SHA256SUMS.txt'
# Same two-space format as sha256sum, so "sha256sum -c" works too
[IO.File]::WriteAllText($sumsPath, "$hash  $zipName`n", [Text.UTF8Encoding]::new($false))

$size = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
$exeSize = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
Write-Host "exe : $exeSize MiB"
Write-Host "zip : $zipPath ($size MiB)"
Write-Host "sha256: $hash"
Write-Host "sums: $sumsPath"
