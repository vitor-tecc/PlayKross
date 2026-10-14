#requires -Version 7.4
param([ValidateRange(10,1800)][int]$Seconds=600, [switch]$GpuSource, [ValidateRange(-1,16)][int]$CaptureIntervalMs=-1,[switch]$VideoOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
New-Item -ItemType Directory -Force (Join-Path $root '.runtime') | Out-Null
try{$probeLock=[IO.FileStream]::new((Join-Path $root '.runtime/live-flow-probe.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Another live-flow probe owns the shared controls.'}
$env:DOTNET_CLI_HOME=Join-Path $root '.tools/dotnet-home'; $env:NUGET_PACKAGES=Join-Path $root '.tools/nuget'
try{
& (Join-Path $root '.tools/dotnet/dotnet.exe') build (Join-Path $root 'tools/acceptance/LiveFlowProbe') --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Sustained publisher build failed.' }
$directory=Join-Path $root '.artifacts/live-flow'; New-Item -ItemType Directory -Force -Path $directory | Out-Null
}catch{$probeLock.Dispose();throw}
$services=$null; $fixture=$null; $prohibited=$null; $publisher=$null; $previous=@{}
foreach($name in @('JKILLER_PROBE_JOIN_LINK','JKILLER_SUSTAINED_SECONDS','JKILLER_PROBE_CAPTURE_MODE')) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
Push-Location $root
try {
    $services=& (Join-Path $PSScriptRoot 'Start-Local.ps1') -PassThru
    foreach($name in @('ready.json','next-preset','stop','sustained-native.json')) { Remove-Item -LiteralPath (Join-Path $directory $name) -ErrorAction SilentlyContinue }
    $fixturePath=if($GpuSource){Join-Path $root '.artifacts/timing-source/Debug/CaptureFixture.exe'}else{Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe'}
    $fixture=Start-Process -FilePath $fixturePath -ArgumentList '--timing' -WindowStyle Hidden -PassThru
    $prohibited=Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -ArgumentList '--prohibited-tone' -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 600
    $environment=@{ JKILLER_PROBE_JOIN_LINK=$services.JoinLink; JKILLER_TARGET_PID=$fixture.Id.ToString(); JKILLER_SUSTAINED_SECONDS=$Seconds.ToString(); DOTNET_CLI_HOME=$env:DOTNET_CLI_HOME; NUGET_PACKAGES=$env:NUGET_PACKAGES }
    $environment['JKILLER_PROBE_CAPTURE_MODE']=if($VideoOnly){'video-only'}else{'application'}
    if($CaptureIntervalMs -ge 0){$environment['JKILLER_WGC_INTERVAL_MS']=$CaptureIntervalMs.ToString()}
    $publisher=Start-Process -FilePath (Join-Path $root '.tools/dotnet/dotnet.exe') -ArgumentList ('"'+(Join-Path $root 'tools/acceptance/LiveFlowProbe/bin/Debug/net10.0-windows10.0.26100.0/LiveFlowProbe.dll')+'"') -WorkingDirectory $root -WindowStyle Hidden -Environment $environment -RedirectStandardOutput (Join-Path $directory 'sustained-publisher.log') -RedirectStandardError (Join-Path $directory 'sustained-errors.log') -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(35)
    while(-not (Test-Path -LiteralPath (Join-Path $directory 'ready.json'))) { if($publisher.HasExited){throw 'Sustained publisher failed; inspect sustained-errors.log.'}; if([DateTime]::UtcNow -ge $deadline){throw 'Publisher readiness deadline exceeded.'}; Start-Sleep -Milliseconds 150 }
    $env:JKILLER_PROBE_JOIN_LINK=$services.JoinLink; $env:JKILLER_SUSTAINED_SECONDS=$Seconds.ToString()
    $env:JKILLER_PROBE_CAPTURE_MODE=$environment['JKILLER_PROBE_CAPTURE_MODE']
    Push-Location (Join-Path $root 'web')
    try { npm exec -- playwright test --config playwright.protocol.config.ts sustained.spec.ts; if($LASTEXITCODE -ne 0){throw 'Sustained measurements failed; inspect numeric report.'} } finally {Pop-Location}
} finally {
    try{
    if($publisher){Set-Content -LiteralPath (Join-Path $directory 'stop') -Value stop}
    if($publisher -and -not $publisher.HasExited){$publisher.WaitForExit(15000)|Out-Null; if(-not $publisher.HasExited){$publisher.Kill($true); $publisher.WaitForExit(5000)|Out-Null}}
    foreach($process in @($fixture,$prohibited)){if($process -and -not $process.HasExited){$process.Kill();$process.WaitForExit(2000)|Out-Null}}
    if($services){& (Join-Path $PSScriptRoot 'Stop-Local.ps1')}
    foreach($name in $previous.Keys){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}; Pop-Location
    }finally{$probeLock.Dispose()}
}
