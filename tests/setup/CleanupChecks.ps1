$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('playkross-cleanup-'+[guid]::NewGuid().ToString('N'))
try {
    foreach($directory in @('.git','config','.tools/dotnet','.tools/npm-cache','.tools/downloads','.artifacts/native/Debug','.artifacts/acceptance','.runtime/obs')) { New-Item -ItemType Directory -Force (Join-Path $fixture $directory) | Out-Null }
    foreach($file in @('JKiller.sln','.tools/dotnet/dotnet.exe','.tools/npm-cache/cache.bin','.tools/downloads/dotnet-10.0.401.zip','.tools/downloads/obs-32.2.2.zip','.artifacts/native/Debug/compile.pch','.artifacts/native/Debug/jk-obs.dll','.artifacts/acceptance/evidence.json','.runtime/obs/obs64.exe')) { [IO.File]::WriteAllText((Join-Path $fixture $file),'keep or regenerate') }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../config/dependencies.json') -Destination (Join-Path $fixture 'config/dependencies.json')
    $script=Join-Path $PSScriptRoot '../../scripts/Optimize-Workspace.ps1'
    if(-not(Test-Path $script)) { throw 'Cleanup feature missing: no safe dry-run command exists.' }
    & $script -Root $fixture | Out-Null
    if(-not(Test-Path (Join-Path $fixture '.tools/npm-cache/cache.bin'))) { throw 'Dry run deleted data.' }
    & $script -Root $fixture -Apply | Out-Null
    foreach($file in @('.tools/npm-cache/cache.bin','.tools/downloads/dotnet-10.0.401.zip','.artifacts/native/Debug/compile.pch')) { if(Test-Path (Join-Path $fixture $file)) { throw "Regenerable file retained: $file" } }
    foreach($file in @('.tools/downloads/obs-32.2.2.zip','.artifacts/native/Debug/jk-obs.dll','.artifacts/acceptance/evidence.json','.runtime/obs/obs64.exe')) { if(-not(Test-Path (Join-Path $fixture $file))) { throw "Protected file removed: $file" } }
    $foreign=Join-Path $fixture 'foreign'; New-Item -ItemType Directory $foreign | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $fixture '.tools/npm-cache') -Target $foreign | Out-Null
    [IO.File]::WriteAllText((Join-Path $foreign 'keep.txt'),'protected')
    $rejected=$false; try { & $script -Root $fixture -Apply | Out-Null } catch { $rejected=$true }
    if(-not $rejected -or -not(Test-Path (Join-Path $foreign 'keep.txt'))) { throw 'Junction was followed or not rejected.' }
    Write-Output 'CleanupChecks: dry run, narrow removal, evidence/runtime preservation, and junction rejection passed.'
} finally {
    $cache=Get-Item -LiteralPath (Join-Path $fixture '.tools/npm-cache') -Force -ErrorAction SilentlyContinue
    if($cache -and ($cache.Attributes -band [IO.FileAttributes]::ReparsePoint)) { [IO.Directory]::Delete($cache.FullName) }
    $resolved=[IO.Path]::GetFullPath($fixture); $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    if(Test-Path $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
