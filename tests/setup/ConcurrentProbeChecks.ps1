$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../scripts/ConcurrentProbeTools.ps1')
. (Join-Path $PSScriptRoot '../../scripts/PortablePackageTools.ps1')
function Reject([scriptblock]$Action) { $rejected=$false; try { & $Action } catch { $rejected=$true }; if(-not $rejected){throw 'Expected unsafe probe input to be rejected.'} }
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('concurrent-check-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $fixture | Out-Null
try {
    $a=Join-Path $fixture 'a'; $b=Join-Path $fixture 'b'
    foreach($instance in @($a,$b)) {
        New-Item -ItemType Directory $instance | Out-Null
        foreach($name in Get-ConcurrentComponentPaths) { $path=Join-Path $instance $name; New-Item -ItemType Directory (Split-Path $path) -Force | Out-Null; Set-Content -LiteralPath $path -Value $name }
    }
    Assert-ConcurrentRoots $a $b
    Reject { Assert-ConcurrentRoots 'relative' $b }; Reject { Assert-ConcurrentRoots $a $a }; Reject { Assert-ConcurrentRoots $a (Join-Path $a 'nested') }
    Set-Content -LiteralPath (Join-Path $b 'PlayKross.dll') -Value mismatch
    Reject { Assert-ConcurrentRoots $a $b }
    Reject { Resolve-ConcurrentMode -VideoOnly -SystemEndpointId endpoint -MonitorSourceId monitor }
    Reject { Resolve-ConcurrentMode -SystemEndpointId endpoint }; Reject { Resolve-ConcurrentMode -MonitorSourceId monitor }
    Reject { Resolve-ConcurrentMode -SystemEndpointId default -MonitorSourceId monitor }
    Reject { Resolve-ConcurrentMode -SystemEndpointId ' ' -MonitorSourceId monitor }
    if((Resolve-ConcurrentMode) -ne 'application' -or (Resolve-ConcurrentMode -VideoOnly) -ne 'video-only' -or (Resolve-ConcurrentMode -SystemEndpointId endpoint -MonitorSourceId monitor) -ne 'system-output'){throw 'Wrong explicit capture mode.'}
    $old=$env:JKILLER_CONCURRENT_CHECK; $env:JKILLER_CONCURRENT_CHECK='prior'
    try { Reject { Invoke-ConcurrentEnvironment @{JKILLER_CONCURRENT_CHECK='temporary'} { if($env:JKILLER_CONCURRENT_CHECK -ne 'temporary'){throw 'Not applied'}; throw 'synthetic failure' } }; if($env:JKILLER_CONCURRENT_CHECK -ne 'prior'){throw 'Environment leaked.'} } finally { $env:JKILLER_CONCURRENT_CHECK=$old }
    $script:stopped=@(); $process=[pscustomobject]@{Id=7;Creation='100';Path='owned.exe'}
    Stop-ConcurrentOwnedProcess @{Id=7;Creation='100';Path='owned.exe'} -Find {param($id) $process} -Stop {param($p) $script:stopped += $p.Id}
    Reject { Stop-ConcurrentOwnedProcess @{Id=7;Creation='99';Path='owned.exe'} -Find {param($id) $process} -Stop {throw 'Must not stop reused PID'} }
    if($script:stopped.Count -ne 1){throw 'Cleanup did not target only owned process.'}
    foreach($path in @('tests/audio-harness/ModuleState.hpp','tests/audio-harness/obs-frontend-api.h','tests/audio-harness/util/platform.h')) {if(-not (Test-PortableNativeSourcePath $path)){throw "Required corresponding source omitted: $path"}}
    foreach($path in @('tests/audio-harness/private.json','tests/audio-harness/logs/private.hpp','tests/../private.hpp')) {if(Test-PortableNativeSourcePath $path){throw 'Native source allowlist too broad.'}}
    $verify=Get-Content (Join-Path $PSScriptRoot '../../scripts/Verify.ps1') -Raw
    if($verify -notmatch 'tests/setup/ConcurrentProbeChecks.ps1' -or $verify -match "&[^\r\n]*Test-ConcurrentBroadcasting"){throw 'CodeOnly must include pure concurrent checks and exclude live runner.'}
    Write-Output 'ConcurrentProbeChecks: PASS roots/matching components, explicit modes, environment restoration, owned cleanup, native source allowlist and CodeOnly boundary (temporary files/fakes only).'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture); $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture cleanup.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
