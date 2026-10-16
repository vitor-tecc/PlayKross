$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $installation){throw 'C++ Build Tools required.'}
$major=(& $vswhere -latest -products '*' -property installationVersion).Split('.')[0]
$generator=if($major -eq '18'){'Visual Studio 18 2026'}else{'Visual Studio 17 2022'}
$cmake=Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
& $cmake -S (Join-Path $root 'native/timing-source') -B (Join-Path $root '.artifacts/timing-source') -G $generator -A x64
if($LASTEXITCODE -ne 0){throw 'Timing source configuration failed.'}
& $cmake --build (Join-Path $root '.artifacts/timing-source') --config Debug --parallel
if($LASTEXITCODE -ne 0){throw 'Timing source build failed.'}
