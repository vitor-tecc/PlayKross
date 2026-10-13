#requires -Version 7.4
param([switch]$Baseline)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'DependencyTools.ps1')
$destination=Join-Path $root '.runtime/obs/bin/64bit/libobs-winrt.dll'
foreach ($process in @(Get-Process -Name obs64 -ErrorAction SilentlyContinue)) {
    if ([string]::Equals($process.Path, (Join-Path $root '.runtime/obs/bin/64bit/obs64.exe'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Stop the owned broadcaster before replacing its capture library.' }
}
$archive=Join-Path $root '.tools/downloads/obs-32.2.2.zip'
Assert-DependencyHash -Path $archive -Algorithm SHA256 -ExpectedHash '4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1'
$original=Join-Path $root '.tools/obs-baseline/libobs-winrt.dll'; New-Item -ItemType Directory -Force -Path (Split-Path $original) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try { [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry('bin/64bit/libobs-winrt.dll'), $original, $true) } finally { $zip.Dispose() }
if ($Baseline) { Copy-Item -LiteralPath $original -Destination $destination; Write-Output 'Restored the hash-verified upstream capture library for baseline measurement.'; return }
& (Join-Path $PSScriptRoot 'Build-Native.ps1') -Plugin
if ($LASTEXITCODE -ne 0) { throw 'Native prerequisites failed.' }
$sourceArchive=Join-Path $root '.tools/downloads/OBS-Studio-32.2.2-Sources.tar.gz'
Assert-DependencyHash -Path $sourceArchive -Algorithm SHA256 -ExpectedHash 'ec81fb66b03e75ddb3076b576f62679c39262e0e9960cef3e17a40dc5d68e6b4'
$patched=Join-Path $root '.tools/instrumented-source'; $component=Join-Path $patched 'libobs-winrt'
New-Item -ItemType Directory -Force -Path $component | Out-Null
tar -xzf $sourceArchive -C $patched --strip-components=1 --exclude='*/build-aux/run-*'
if ($LASTEXITCODE -ne 0) { throw 'Verified OBS source extraction failed.' }
$source=$patched
$patch=Join-Path $root 'patches/obs-32.2.2-capture-trace.patch'
& git -C $patched apply --check --ignore-space-change $patch
if ($LASTEXITCODE -ne 0) { throw 'Pinned capture patch did not apply cleanly.' }
& git -C $patched apply --ignore-space-change $patch
if ($LASTEXITCODE -ne 0) { throw 'Pinned capture patch application failed.' }
$vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ Build Tools and the Windows SDK are required.' }
$cmake=Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$major=(& $vswhere -latest -products '*' -property installationVersion).Split('.')[0]
$generator=if ($major -eq '18') { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
$build=Join-Path $root '.artifacts/instrumented-obs'
& $cmake -S (Join-Path $root 'native/instrumented-obs') -B $build -G $generator -A x64 ("-DJK_PATCHED_SOURCE=$component") ("-DJK_OBS_SOURCE=$source") ("-DJK_OBS_IMPORT="+(Join-Path $root '.artifacts/native/imports')) ("-DJK_GENERATED_HEADERS="+(Join-Path $root '.artifacts/native/generated'))
if ($LASTEXITCODE -ne 0) { throw 'Instrumented capture configuration failed.' }
& $cmake --build $build --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw 'Instrumented capture build failed.' }
$binary=Join-Path $build 'Release/libobs-winrt.dll'; Copy-Item -LiteralPath $binary -Destination $destination
[ordered]@{ component='libobs-winrt'; upstream='OBS32.2.2'; sourceArchiveSha256=(Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash; patchSha256=(Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash; binarySha256=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash; gpuReadbackAdded=$false; license='GPL-2.0-or-later'; notice='Existing OBS texture copies and frame pool retained; added texture sequence/time queries and supported-OS WGC4ms minimum-update request/readback.' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $build 'provenance.json')
Write-Output 'Installed only the owned instrumented WGC library. Existing public OBS packet timing APIs cover raw and GPU encoding; unused DXGI acquisition remains unsupported.'
