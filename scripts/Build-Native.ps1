param([switch]$Test,[switch]$Plugin)
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
$extra = @('-DJK_BUILD_OBS_PLUGIN=OFF')
if ($Plugin) {
    . (Join-Path $PSScriptRoot 'DependencyTools.ps1')
    $sourceArchive = Join-Path $projectRoot '.tools/downloads/OBS-Studio-32.2.2-Sources.tar.gz'
    if (-not (Test-Path -LiteralPath $sourceArchive)) { Invoke-WebRequest -Uri 'https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Sources.tar.gz' -OutFile $sourceArchive }
    Assert-DependencyHash -Path $sourceArchive -Algorithm SHA256 -ExpectedHash 'ec81fb66b03e75ddb3076b576f62679c39262e0e9960cef3e17a40dc5d68e6b4'
    $sourceParent = Join-Path $projectRoot '.tools/obs-source'
    $sourceRoot = Join-Path $sourceParent 'obs-studio-32.2.2-sources'
    $jsonHeader = Join-Path $projectRoot '.tools/json/include/nlohmann/json.hpp'
    if (-not (Test-Path -LiteralPath $jsonHeader)) {
        New-Item -ItemType Directory -Path (Split-Path $jsonHeader) -Force | Out-Null
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/nlohmann/json/v3.12.0/single_include/nlohmann/json.hpp' -OutFile $jsonHeader
    }
    Assert-DependencyHash -Path $jsonHeader -Algorithm SHA256 -ExpectedHash 'aaf127c04cb31c406e5b04a63f1ae89369fccde6d8fa7cdda1ed4f32dfc5de63'
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'libobs/obs.h'))) {
        New-Item -ItemType Directory -Path $sourceParent -Force | Out-Null
        tar -xzf $sourceArchive -C $sourceParent --exclude='*/build-aux/run-*'
        if ($LASTEXITCODE -ne 0) { throw 'OBS source extraction failed.' }
    }
    $toolVersion = Get-ChildItem -LiteralPath (Join-Path $installation 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $compilerTools = Join-Path $toolVersion.FullName 'bin/Hostx64/x64'
    $imports = Join-Path $buildRoot 'imports'
    New-Item -ItemType Directory -Path $imports -Force | Out-Null
    foreach ($library in @('obs','obs-frontend-api')) {
        $binary = Join-Path $projectRoot ('.runtime/obs/bin/64bit/' + $library + '.dll')
        $exportLines = & (Join-Path $compilerTools 'dumpbin.exe') /nologo /exports $binary
        if ($LASTEXITCODE -ne 0) { throw "Cannot inspect pinned $library exports." }
        $exports = foreach ($line in $exportLines) { if ($line -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+(\S+)') { $Matches[1] } }
        if (-not $exports) { throw "No exports found for $library." }
        $definition = Join-Path $imports ($library + '.def')
        @(( 'LIBRARY ' + $library + '.dll'), 'EXPORTS') + $exports | Set-Content -LiteralPath $definition -Encoding ascii
        & (Join-Path $compilerTools 'lib.exe') /nologo ("/DEF:" + $definition) ("/OUT:" + (Join-Path $imports ($library + '.lib'))) /MACHINE:X64
        if ($LASTEXITCODE -ne 0) { throw 'OBS import library generation failed.' }
    }
    $extra = @('-DJK_BUILD_OBS_PLUGIN=ON',('-DJK_OBS_SOURCE_ROOT=' + $sourceRoot),('-DJK_OBS_IMPORT_ROOT=' + $imports),('-DJK_JSON_INCLUDE_ROOT=' + (Join-Path $projectRoot '.tools/json/include')))
}
& $cmake -S (Join-Path $projectRoot 'native/jk-obs') -B $buildRoot -G $generator -A x64 @extra
if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed.' }
& $cmake --build $buildRoot --config Debug --parallel
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
if ($Plugin) {
    Copy-Item -LiteralPath (Join-Path $buildRoot 'Debug/jk-obs.dll') -Destination (Join-Path $projectRoot '.runtime/obs/obs-plugins/64bit/jk-obs.dll')
}
if ($Test) {
    & $ctest --test-dir $buildRoot -C Debug --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
}
