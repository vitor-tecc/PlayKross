$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../scripts/TemporaryTunnel.ps1')
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start()
try{
    $rejected=$false;try{$reservation=New-TemporaryTunnelPortReservation -Port $listener.LocalEndpoint.Port;$reservation.Stop()}catch{$rejected=$true}
    if(-not $rejected){throw 'Foreign listener could be published before ownership was established.'}
}finally{$listener.Stop()}
$current=[pscustomobject]@{Pid=101;CreationFileTime='123';ExecutablePath='C:/fixture/cloudflared.exe'}
$expected=[pscustomobject]@{Pid=202;CreationFileTime='456';ExecutablePath='C:/fixture/cloudflared.exe'}
$rejected=$false;try{Assert-ExpectedTunnelRun $current $expected}catch{$rejected=$true}
if(-not $rejected){throw 'Old owner accepted a replacement tunnel run.'}
Assert-ExpectedTunnelRun $current $current
$fixtureRoot=Join-Path $PSScriptRoot ('../../.artifacts/lifecycle-check-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $fixtureRoot 'scripts'),(Join-Path $fixtureRoot '.artifacts/live-flow') | Out-Null
foreach($scriptName in @('Test-TemporaryVideo.ps1','Test-PublicationCrash.ps1','Test-LiveFlow.ps1')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('../../scripts/'+$scriptName)) -Destination (Join-Path $fixtureRoot ('scripts/'+$scriptName))
    foreach($launch in @('Start-TemporaryTunnel.ps1','Start-Local.ps1')){'throw "Launch refused: fixture already owned."' | Set-Content (Join-Path $fixtureRoot ('scripts/'+$launch))}
    $control=Join-Path $fixtureRoot '.artifacts/live-flow'
    'existing-ready' | Set-Content (Join-Path $control 'ready.json')
    Remove-Item -LiteralPath (Join-Path $control 'stop') -ErrorAction SilentlyContinue
    $rejected=$false;try{& (Join-Path $fixtureRoot ('scripts/'+$scriptName))}catch{$rejected=$true}
    if(-not $rejected){throw 'Fixture launch unexpectedly succeeded.'}
    if(-not(Test-Path (Join-Path $control 'ready.json')) -or (Get-Content -Raw (Join-Path $control 'ready.json')).Trim() -ne 'existing-ready' -or (Test-Path (Join-Path $control 'stop'))){throw "$scriptName modified another run's controls after ownership refusal."}
}
$probeLock=[IO.FileStream]::new((Join-Path $fixtureRoot '.runtime/live-flow-probe.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{
    foreach($scriptName in @('Test-Sustained.ps1','Test-FirefoxViewing.ps1')){
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('../../scripts/'+$scriptName)) -Destination (Join-Path $fixtureRoot ('scripts/'+$scriptName))
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../scripts/DependencyTools.ps1') -Destination (Join-Path $fixtureRoot 'scripts/DependencyTools.ps1')
        $message='';try{& (Join-Path $fixtureRoot ('scripts/'+$scriptName))}catch{$message=$_.Exception.Message}
        if($message -notmatch 'shared controls'){throw "$scriptName did work before checking active probe ownership."}
    }
}finally{$probeLock.Dispose()}
Write-Output 'TunnelLifecycleChecks: foreign port, replacement ownership and refused-test control preservation passed.'
