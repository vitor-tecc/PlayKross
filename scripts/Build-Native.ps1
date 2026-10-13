param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vswhere = 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ Build Tools with the Windows SDK.' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'The C++ build-tools component is missing.' }
$cmakeDirectory = Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin'
$cmake = Join-Path $cmakeDirectory 'cmake.exe'
$ctest = Join-Path $cmakeDirectory 'ctest.exe'
$major = (& $vswhere -latest -products '*' -property installationVersion).Split('.')[0]
$generator = if ($major -eq '18') { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
$buildRoot = Join-Path $projectRoot '.artifacts/native'
& $cmake -S (Join-Path $projectRoot 'native/jk-obs') -B $buildRoot -G $generator -A x64
if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed.' }
& $cmake --build $buildRoot --config Debug --parallel
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
if ($Test) {
    & $ctest --test-dir $buildRoot -C Debug --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
}
