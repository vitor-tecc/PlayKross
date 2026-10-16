#requires -Version 7.4
param([switch]$Apply,[string]$Root=(Join-Path $PSScriptRoot '..'))
$ErrorActionPreference='Stop'
$rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')
$prefix=$rootPath+[IO.Path]::DirectorySeparatorChar
foreach($marker in @('JKiller.sln','config/dependencies.json','.git')) { if(-not(Test-Path -LiteralPath (Join-Path $rootPath $marker))) { throw 'Expected PlayKross workspace markers are missing.' } }
function Assert-SafePath([string]$Path) {
    $resolved=[IO.Path]::GetFullPath($Path)
    if(-not $resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target leaves the workspace.' }
    $cursor=$resolved
    while($cursor) {
        $item=Get-Item -LiteralPath $cursor -Force -ErrorAction SilentlyContinue
        if($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Cleanup refuses links/junctions: $cursor" }
        if($cursor -eq $rootPath) { break }; $cursor=Split-Path $cursor -Parent
    }
}
# Only extracted, redundant SDK/server archives; preserve OBS archives for the pinned capture rebuild.
$manifest=Get-Content -LiteralPath (Join-Path $rootPath 'config/dependencies.json') -Raw | ConvertFrom-Json -AsHashtable
$candidates=[Collections.Generic.List[string]]::new()
foreach($id in @('dotnet','mediamtx','caddy')) {
    if($manifest.ContainsKey($id)) {
        $entry=$manifest[$id]
        if(Test-Path -LiteralPath (Join-Path $rootPath ($entry.destination+'/'+$entry.executable))) { $candidates.Add((Join-Path $rootPath ('.tools/downloads/'+$id+'-'+$entry.version+'.zip'))) }
    }
}
$cache=Join-Path $rootPath '.tools/npm-cache'; Assert-SafePath $cache
if(Test-Path -LiteralPath $cache) {
    # Reject all reparse entries before any mutation; never traverse one.
    $cacheItems=@(Get-ChildItem -LiteralPath $cache -Recurse -Force)
    foreach($item in $cacheItems) { Assert-SafePath $item.FullName }
    $candidates.Add($cache)
}
$build=Join-Path $rootPath '.artifacts'; Assert-SafePath $build
if(Test-Path -LiteralPath $build) {
    # Compilation/link intermediates regenerate on the next build; keep DLL/EXE, PDB, logs and JSON evidence.
    foreach($item in @(Get-ChildItem -LiteralPath $build -Recurse -Force)) {
        Assert-SafePath $item.FullName
        if(-not $item.PSIsContainer -and $item.Extension -in @('.pch','.obj','.ilk','.ipdb','.iobj')) { $candidates.Add($item.FullName) }
    }
}
$targets=@($candidates | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object -Unique)
foreach($path in $targets) { Assert-SafePath $path }
$total=0L
foreach($path in $targets) {
    $item=Get-Item -LiteralPath $path -Force
    $bytes=if($item.PSIsContainer){(Get-ChildItem -LiteralPath $path -Recurse -File -Force | Measure-Object Length -Sum).Sum}else{$item.Length}
    $total+=$bytes
    [pscustomobject]@{Mode=if($Apply){'Remove'}else{'DryRun'};Path=[IO.Path]::GetRelativePath($rootPath,$path);MiB=[math]::Round($bytes/1MB,2)}
}
if($Apply) {
    foreach($process in @(Get-Process -ErrorAction SilentlyContinue)) {
        try { $program=$process.Path } catch { continue }
        if($program -and ($program.StartsWith((Join-Path $rootPath '.runtime')+'\',[StringComparison]::OrdinalIgnoreCase) -or $program.StartsWith($build+'\',[StringComparison]::OrdinalIgnoreCase))) { throw 'Stop workspace runtime and test helpers before cleaning.' }
    }
    foreach($path in $targets) { Assert-SafePath $path; Remove-Item -LiteralPath $path -Force -Recurse }
}
Write-Output ("{0}: {1:N2} MiB. Runtime, source, package installs, evidence, capture rebuild archives and provenance retained." -f $(if($Apply){'Removed'}else{'Available to remove'}),($total/1MB))
