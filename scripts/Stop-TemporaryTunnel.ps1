#requires -Version 7.4
param([object]$ExpectedTunnel)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'TemporaryTunnel.ps1')
$state=Join-Path $root '.runtime/temporary-tunnel.local.json'
if(-not(Test-Path $state)){Write-Output 'No tracked temporary tunnel.';return}
try{$lifecycleLock=[IO.FileStream]::new((Join-Path $root '.runtime/tunnel-launch.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
catch{throw 'A tunnel lifecycle operation is in progress; retry when it finishes.'}
try{
if(-not(Test-Path $state)){return}
$tracked=Get-Content -Raw $state | ConvertFrom-Json
Assert-ExpectedTunnelRun $tracked.Tunnel $ExpectedTunnel
$executable=Join-Path $root '.tools/cloudflared/cloudflared.exe'
$process=Get-Process -Id $tracked.Tunnel.Pid -ErrorAction SilentlyContinue
if($process){
    if(-not(Test-OwnedTunnelProcess $tracked.Tunnel $executable)){throw 'Tunnel process identity changed; refusing to stop it.'}
    $process.Kill();$process.WaitForExit(5000)|Out-Null
    if(-not $process.HasExited){throw 'Owned tunnel did not stop.'}
}
$serviceState=Join-Path $root '.runtime/local-services.local.json'
if(Test-Path $serviceState){
    $current=@(Get-Content -Raw $serviceState | ConvertFrom-Json)
    if($current.Count -ne @($tracked.Processes).Count){throw 'Local services changed; they will not be stopped.'}
    foreach($entry in $current){
        if(@($tracked.Processes | Where-Object { $_.Role -eq $entry.Role -and $_.Pid -eq $entry.Pid -and $_.CreationFileTime -eq $entry.CreationFileTime -and $_.ExecutablePath -eq $entry.ExecutablePath }).Count -ne 1){throw 'Local services changed; they will not be stopped.'}
    }
    & (Join-Path $PSScriptRoot 'Stop-Local.ps1')
}
Remove-Item -LiteralPath $state
Write-Output 'Stopped only this tracked temporary tunnel and its matching services.'
}finally{$lifecycleLock.Dispose()}
