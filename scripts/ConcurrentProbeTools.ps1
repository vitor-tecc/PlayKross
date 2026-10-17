# Pure boundaries shared by the local functional runner and CodeOnly checks.
. (Join-Path $PSScriptRoot 'PortablePackageTools.ps1')
. (Join-Path $PSScriptRoot 'ConcurrentMonitorFixture.ps1')
function Get-ConcurrentComponentPaths { @('BUILD-INFO.json','PlayKross.exe','PlayKross.dll','JKiller.Windows.dll','JKiller.Core.dll','JKiller.Contracts.dll','.runtime/obs/bin/64bit/obs64.exe','.runtime/obs/bin/64bit/libobs-winrt.dll','.runtime/obs/obs-plugins/64bit/jk-obs.dll','config/dependencies.json','config/obs/allowed-plugins.txt','config/obs/global.ini','config/obs/user.ini','config/obs/basic/profiles/JKiller/basic.ini','config/obs/basic/scenes/JKiller.json','config/obs/plugin_config/obs-websocket/config.json') }
function Get-ConcurrentRuntimePaths([string]$Root) {
    $paths=@(Get-ConcurrentComponentPaths)
    foreach($file in Get-ChildItem -LiteralPath $Root -File | Where-Object Extension -in '.dll','.exe'){$paths += $file.Name}
    foreach($directory in @('.runtime/obs/bin','.runtime/obs/data','.runtime/obs/obs-plugins')) {
        $location=Join-Path $Root $directory
        if(Test-Path -LiteralPath $location){foreach($file in Get-ChildItem -LiteralPath $location -Recurse -File){$paths += [IO.Path]::GetRelativePath($Root,$file.FullName).Replace('\','/')}}
    }
    @($paths | Sort-Object -Unique)
}
function Assert-ConcurrentRoots([string]$A,[string]$B) {
    foreach($path in @($A,$B)){if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Instance roots must be absolute.'}; Assert-PortableDirectory $path ([IO.Path]::GetPathRoot($path)); if(-not (Test-Path -LiteralPath $path -PathType Container)){throw 'Prepared portable root missing.'}}
    $left=[IO.Path]::GetFullPath($A).TrimEnd('\','/'); $right=[IO.Path]::GetFullPath($B).TrimEnd('\','/')
    if($left -eq $right -or $left.StartsWith($right+'\',[StringComparison]::OrdinalIgnoreCase) -or $right.StartsWith($left+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Instance roots must be distinct and non-nested.'}
    $leftPaths=@(Get-ConcurrentRuntimePaths $left); $rightPaths=@(Get-ConcurrentRuntimePaths $right)
    if(@(Compare-Object $leftPaths $rightPaths).Count){throw 'Portable runtime inventories differ.'}
    foreach($name in $leftPaths){foreach($path in @($left,$right)){Assert-PortableFile (Join-Path $path $name) $path}; if((Get-FileHash -LiteralPath (Join-Path $left $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $right $name)).Hash){throw "Portable components differ: $name"}}
}
function Resolve-ConcurrentMode([switch]$VideoOnly,[string]$SystemEndpointId,[string]$MonitorSourceId) {
    if(($SystemEndpointId -and ([string]::IsNullOrWhiteSpace($SystemEndpointId) -or $SystemEndpointId.Trim() -eq 'default')) -or ($MonitorSourceId -and [string]::IsNullOrWhiteSpace($MonitorSourceId))){throw 'System output requires concrete endpoint and monitor IDs.'}
    if(($VideoOnly -and ($SystemEndpointId -or $MonitorSourceId)) -or ([bool]$SystemEndpointId -ne [bool]$MonitorSourceId)){throw 'Choose application (default), VideoOnly, or a concrete SystemEndpointId together with MonitorSourceId.'}
    if($SystemEndpointId){return 'system-output'}; if($VideoOnly){return 'video-only'}; return 'application'
}
function Invoke-ConcurrentEnvironment([hashtable]$Values,[scriptblock]$Action) {
    $previous=@{}; foreach($key in $Values.Keys){$previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process')}
    try{foreach($key in $Values.Keys){[Environment]::SetEnvironmentVariable($key,[string]$Values[$key],'Process')}; & $Action}finally{foreach($key in $previous.Keys){[Environment]::SetEnvironmentVariable($key,$previous[$key],'Process')}}
}
function Stop-ConcurrentOwnedProcess($Identity,[scriptblock]$Find={param($id) $p=Get-Process -Id $id -ErrorAction SilentlyContinue; if($p){[pscustomobject]@{Id=$p.Id;Creation=$p.StartTime.ToFileTimeUtc().ToString();Path=$p.MainModule.FileName;Process=$p}}},[scriptblock]$Stop={param($p) if(-not $p.Process.HasExited){$p.Process.Kill($true);$p.Process.WaitForExit(5000)|Out-Null}}) {
    $current=& $Find $Identity.Id; if(-not $current){return}; if($current.Creation -ne $Identity.Creation -or $current.Path -ne $Identity.Path){throw 'Owned cleanup refused: process identity changed.'}; & $Stop $current
}
