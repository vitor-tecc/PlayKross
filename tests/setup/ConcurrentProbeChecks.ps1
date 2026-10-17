$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../scripts/ConcurrentProbeTools.ps1')
. (Join-Path $PSScriptRoot '../../scripts/PortablePackageTools.ps1')
function Reject([scriptblock]$Action) { $rejected=$false; try { & $Action } catch { $rejected=$true }; if(-not $rejected){throw 'Expected unsafe probe input to be rejected.'} }
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('concurrent-check-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $fixture | Out-Null
$fixtureLinks=[Collections.Generic.List[object]]::new()
function Remove-FixtureLink([string]$Path,[bool]$Directory) {
    $resolvedLink=[IO.Path]::GetFullPath($Path);$boundary=[IO.Path]::GetFullPath($fixture).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $resolvedLink.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe link cleanup target.'}
    $item=Get-Item -LiteralPath $resolvedLink -Force -ErrorAction SilentlyContinue
    if($item){
        if(-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Refusing link cleanup of a regular entry.'}
        if($Directory){[IO.Directory]::Delete($resolvedLink)}else{[IO.File]::Delete($resolvedLink)}
    }
}
function Invoke-FileLinkMetadataFixture([string]$RootA,[string]$RootB,[string]$AliasPath,[string]$TargetPath,[bool]$Dangling) {
    # Native file symlinks need administrator privilege on this machine. Model
    # only that filesystem read boundary while exercising actual root preflight.
    function Get-Item {
        [CmdletBinding()]param([string]$LiteralPath,[switch]$Force)
        if([IO.Path]::GetFullPath($LiteralPath) -eq [IO.Path]::GetFullPath($AliasPath)){
            return [pscustomobject]@{FullName=$LiteralPath;Attributes=[IO.FileAttributes]::ReparsePoint;PSIsContainer=$false;LinkType='SymbolicLink';LinkTarget=$TargetPath;Exists=(-not $Dangling)}
        }
        Microsoft.PowerShell.Management\Get-Item @PSBoundParameters
    }
    Assert-ConcurrentRoots $RootA $RootB
}
try {
    $a=Join-Path $fixture 'a'; $b=Join-Path $fixture 'b'
    foreach($instance in @($a,$b)) {
        New-Item -ItemType Directory $instance | Out-Null
        foreach($name in Get-ConcurrentComponentPaths) { $path=Join-Path $instance $name; New-Item -ItemType Directory (Split-Path $path) -Force | Out-Null; Set-Content -LiteralPath $path -Value $name }
    }
    Assert-ConcurrentRoots $a $b
    # Different regular previous profiles/locks are allowed and must remain untouched.
    $priorState=@{}
    foreach($instance in @($a,$b)) {
        foreach($relative in @('.runtime/obs/config/obs-studio/basic/profiles/JKiller/previous.txt','.runtime/webview/origin/Default/Preferences','.runtime/concurrent-probe.lock','.runtime/obs/jkiller-owner.lock')){
            $state=Join-Path $instance $relative;New-Item -ItemType Directory (Split-Path $state) -Force | Out-Null;Set-Content -LiteralPath $state -Value $instance;$priorState[$state]=$instance
        }
    }
    Assert-ConcurrentRoots $a $b
    if((Get-Content -LiteralPath (Join-Path $a '.runtime/webview/origin/Default/Preferences')) -ne $a){throw 'Regular prior state changed.'}
    $aliasFailures=@();$fileMetadataFallbacks=0
    $aliasCases=@(
        @{Name='OBS config A-to-B';Path='.runtime/obs/config';Directory=$true;IntoB=$true},
        @{Name='nested OBS profile';Path='.runtime/obs/config/obs-studio/basic/profiles/JKiller';Directory=$true},
        @{Name='WebView subtree';Path='.runtime/webview';Directory=$true},
        @{Name='nested WebView profile';Path='.runtime/webview/origin/Default/cache';Directory=$true},
        @{Name='runner lock symlink';Path='.runtime/concurrent-probe.lock';Directory=$false},
        @{Name='OBS owner lock symlink';Path='.runtime/obs/jkiller-owner.lock';Directory=$false},
        @{Name='dangling runner lock';Path='.runtime/concurrent-probe.lock';Directory=$false;Dangling=$true},
        @{Name='dangling OBS lock';Path='.runtime/obs/jkiller-owner.lock';Directory=$false;Dangling=$true}
    )
    foreach($case in $aliasCases){
        $caseRoot=Join-Path $fixture ([guid]::NewGuid().ToString('N'));$caseA=Join-Path $caseRoot 'a';$caseB=Join-Path $caseRoot 'b'
        foreach($instance in @($caseA,$caseB)){foreach($name in Get-ConcurrentComponentPaths){$path=Join-Path $instance $name;New-Item -ItemType Directory (Split-Path $path) -Force | Out-Null;Set-Content -LiteralPath $path -Value $name}}
        $target=if($case.IntoB){Join-Path $caseB '.runtime/obs/config'}else{Join-Path $caseRoot 'unrelated-state'}
        $sentinel=if($case.Directory){New-Item -ItemType Directory $target -Force | Out-Null;Join-Path $target 'keep.txt'}else{$target}
        if(-not $case.Dangling){Set-Content -LiteralPath $sentinel -Value 'preserve target'}
        $link=Join-Path $caseA $case.Path;New-Item -ItemType Directory (Split-Path $link) -Force | Out-Null
        $kind=if($case.Directory){'Junction'}else{'SymbolicLink'}
        $metadataOnly=$false
        try{New-Item -ItemType $kind -Path $link -Target $target | Out-Null}catch{
            if(-not $case.Directory -and $_.FullyQualifiedErrorId -like 'NewItemSymbolicLinkElevationRequired,*'){$metadataOnly=$true;$fileMetadataFallbacks++}else{throw}
        }
        if(-not $metadataOnly){$fixtureLinks.Add(@{Path=$link;Directory=[bool]$case.Directory})}
        try {
            if(-not $metadataOnly){$linkItem=Get-Item -LiteralPath $link -Force;if(-not ($linkItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Alias fixture did not create a reparse point.'}}
            $rejected=$false
            try{if($metadataOnly){Invoke-FileLinkMetadataFixture $caseA $caseB $link $target ([bool]$case.Dangling)}else{Assert-ConcurrentRoots $caseA $caseB}}catch{if($_.Exception.Message -match 'reparse|junction|symbolic'){$rejected=$true}else{throw}}
            if(-not $rejected){$aliasFailures += $case.Name}
            if(-not $case.Dangling -and (Get-Content -LiteralPath $sentinel) -ne 'preserve target'){throw 'Validation modified linked target state.'}
        } finally {if(-not $metadataOnly){Remove-FixtureLink $link ([bool]$case.Directory)}}
        if(-not $case.Dangling -and (Get-Content -LiteralPath $sentinel) -ne 'preserve target'){throw 'Link-only cleanup changed its target.'}
    }
    Assert-ConcurrentRoots $a $b
    foreach($state in $priorState.Keys){if((Get-Content -LiteralPath $state) -ne $priorState[$state]){throw 'Regular prior state was changed or removed.'}}
    if($fileMetadataFallbacks){Write-Output "ConcurrentProbeChecks: $fileMetadataFallbacks linked/dangling lock cases used scoped Get-Item metadata fakes; native file-symlink creation requires unavailable administrator privilege. Real directory junctions exercised separately; mock scope restored."}
    if($aliasFailures.Count){throw ('Mutable aliases accepted: '+($aliasFailures -join ', '))}
    foreach($instance in @($a,$b)){Set-Content -LiteralPath (Join-Path $instance '.runtime/obs/bin/64bit/additional.dll') -Value $instance}
    Reject { Assert-ConcurrentRoots $a $b }
    foreach($instance in @($a,$b)){Set-Content -LiteralPath (Join-Path $instance '.runtime/obs/bin/64bit/additional.dll') -Value same}
    Reject { Assert-ConcurrentRoots 'relative' $b }; Reject { Assert-ConcurrentRoots $a $a }; Reject { Assert-ConcurrentRoots $a (Join-Path $a 'nested') }
    Set-Content -LiteralPath (Join-Path $b 'PlayKross.dll') -Value mismatch
    Reject { Assert-ConcurrentRoots $a $b }
    Reject { Resolve-ConcurrentMode -VideoOnly -SystemEndpointId endpoint -MonitorSourceId monitor }
    Reject { Resolve-ConcurrentMode -SystemEndpointId endpoint }; Reject { Resolve-ConcurrentMode -MonitorSourceId monitor }
    Reject { Resolve-ConcurrentMode -SystemEndpointId default -MonitorSourceId monitor }
    Reject { Resolve-ConcurrentMode -SystemEndpointId ' ' -MonitorSourceId monitor }
    if((Resolve-ConcurrentMode) -ne 'application' -or (Resolve-ConcurrentMode -VideoOnly) -ne 'video-only' -or (Resolve-ConcurrentMode -SystemEndpointId endpoint -MonitorSourceId monitor) -ne 'system-output'){throw 'Wrong explicit capture mode.'}
    $old=$env:JKILLER_CONCURRENT_CHECK; $env:JKILLER_CONCURRENT_CHECK='prior'
    $script:observedScoped=$false
    try { Reject { Invoke-ConcurrentEnvironment @{JKILLER_CONCURRENT_CHECK='temporary'} { $script:observedScoped=$env:JKILLER_CONCURRENT_CHECK -eq 'temporary'; throw 'synthetic failure' } }; if(-not $script:observedScoped -or $env:JKILLER_CONCURRENT_CHECK -ne 'prior'){throw 'Environment not applied/restored.'} } finally { $env:JKILLER_CONCURRENT_CHECK=$old }
    $script:stopped=@(); $process=[pscustomobject]@{Id=7;Creation='100';Path='owned.exe'}
    Stop-ConcurrentOwnedProcess @{Id=7;Creation='100';Path='owned.exe'} -Find {param($id) $process} -Stop {param($p) $script:stopped += $p.Id}
    Reject { Stop-ConcurrentOwnedProcess @{Id=7;Creation='99';Path='owned.exe'} -Find {param($id) $process} -Stop {throw 'Must not stop reused PID'} }
    if($script:stopped.Count -ne 1){throw 'Cleanup did not target only owned process.'}
    $owner=@{Id=7;Creation='100';Path='owned.exe'}
    $surface=@{Id=7;Creation='100';Path='owned.exe';WindowPid=7;Visible=$true;Topmost=$true;Borderless=$true;Bounds=@(0,0,1440,2560)}
    Assert-ConcurrentMonitorSurface $owner $surface @(0,0,1440,2560)
    foreach($key in @('Id','Creation','Path','WindowPid','Visible','Topmost','Borderless','Bounds')){
        $bad=$surface.Clone();$bad[$key]=switch($key){Id {8};Creation {'99'};Path {'foreign.exe'};WindowPid {8};Bounds {@(1,0,1440,2560)};default {$false}}
        Reject {Assert-ConcurrentMonitorSurface $owner $bad @(0,0,1440,2560)}
    }
    foreach($path in @('tests/audio-harness/ModuleState.hpp','tests/audio-harness/obs-frontend-api.h','tests/audio-harness/util/platform.h')) {if(-not (Test-PortableNativeSourcePath $path)){throw "Required corresponding source omitted: $path"}}
    foreach($path in @('tests/audio-harness/private.json','tests/audio-harness/logs/private.hpp','tests/../private.hpp')) {if(Test-PortableNativeSourcePath $path){throw 'Native source allowlist too broad.'}}
    $verify=Get-Content (Join-Path $PSScriptRoot '../../scripts/Verify.ps1') -Raw
    if($verify -notmatch 'tests/setup/ConcurrentProbeChecks.ps1' -or $verify -match "&[^\r\n]*Test-ConcurrentBroadcasting"){throw 'CodeOnly must include pure concurrent checks and exclude live runner.'}
    Write-Output 'ConcurrentProbeChecks: PASS roots/matching components, 8 mutable-profile/linked-lock alias refusals, prior-state/target preservation, explicit modes, environment restoration, owned cleanup, native source allowlist and CodeOnly boundary (temporary files/fakes only).'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture); $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture cleanup.'}
    foreach($link in $fixtureLinks){Remove-FixtureLink $link.Path $link.Directory}
    # Check one level at a time so cleanup never descends through an untracked alias.
    $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($resolved)
    while($pending.Count){foreach($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force){if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refusing recursive fixture cleanup with a remaining reparse point.'};if($entry.PSIsContainer){$pending.Push($entry.FullName)}}}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
