<#
Builds the release zip and SHA256SUMS.txt under dist/.

  pwsh packaging/make_release.ps1 -Version 0.1.0

Output:
  dist/ComPortWidget-<Version>-win64.zip
  dist/SHA256SUMS.txt
The zip holds one folder, ComPortWidget-<Version>/, with ComPortWidget.exe, LICENSE, NOTICE.md,
NOTICE.en.md and THIRD_PARTY_NOTICES.md. The app targets .NET Framework 4.8 (part of Windows 10 1903
and later), so nothing else is bundled. The build must produce ComPortWidget.exe and nothing else.
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
$folderName = "ComPortWidget-$Version"
$stageFolder = Join-Path $stage $folderName
$zipName = "ComPortWidget-$Version-win64.zip"
$zipPath = Join-Path $dist $zipName

foreach ($d in @($publish, $stage)) {
    if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
}
foreach ($f in @($zipPath, (Join-Path $dist 'SHA256SUMS.txt'))) {
    if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
}
New-Item -ItemType Directory -Force -Path $publish, $stageFolder | Out-Null

Write-Host "Building version $Version ..."
& dotnet build (Join-Path $repo 'src/ComPortWidget.App') -c Release -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }

# The output must be exactly one file: the exe (Core and Platform are compiled into it)
$produced = @(Get-ChildItem -LiteralPath $publish -File | ForEach-Object { $_.Name })
if ($produced.Count -ne 1 -or $produced[0] -ne 'ComPortWidget.exe') {
    throw "Unexpected build output: $($produced -join ', ')"
}
$exe = Join-Path $publish 'ComPortWidget.exe'

Copy-Item -LiteralPath $exe -Destination $stageFolder
foreach ($name in @('LICENSE', 'NOTICE.md', 'NOTICE.en.md', 'THIRD_PARTY_NOTICES.md')) {
    $src = Join-Path $repo $name
    if (-not (Test-Path -LiteralPath $src)) { throw "Missing file: $name" }
    Copy-Item -LiteralPath $src -Destination $stageFolder
}

# Compress-Archive on Windows PowerShell 5.1 writes backslash entry names; use .NET so entries use '/'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $stageFolder -File) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, "$folderName/$($file.Name)", [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $zip.Dispose() }

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$sumsPath = Join-Path $dist 'SHA256SUMS.txt'
# Same two-space format as sha256sum, so "sha256sum -c" works too
[IO.File]::WriteAllText($sumsPath, "$hash  $zipName`n", [Text.UTF8Encoding]::new($false))

$exeBytes = (Get-Item -LiteralPath $exe).Length
$zipBytes = (Get-Item -LiteralPath $zipPath).Length
Write-Host "exe : $exeBytes bytes"
Write-Host "zip : $zipPath ($zipBytes bytes)"
Write-Host "sha256: $hash"
Write-Host "sums: $sumsPath"
