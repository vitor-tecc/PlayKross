$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$foreign = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 5000)
$foreign.Start()
try {
    $rejected = $false
    try { & (Join-Path $root 'scripts/Start-Local.ps1') -NoBuild -PassThru | Out-Null }
    catch { $rejected = $_.Exception.Message -match '5000.*occupied' }
    if (-not $rejected) { throw 'Occupied port did not produce the expected actionable rejection.' }
    if (-not $foreign.Server.IsBound) { throw 'Foreign listener was stopped.' }
    Write-Output 'PASS: occupied port rejected without stopping the foreign listener.'
}
finally { $foreign.Stop() }
$launchLock = [IO.FileStream]::new((Join-Path $root '.runtime/local-launch.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $rejected = $false
    try { & (Join-Path $root 'scripts/Start-Local.ps1') -NoBuild -PassThru | Out-Null }
    catch { $rejected = $_.Exception.Message -match 'launch.*progress' }
    if (-not $rejected) { throw 'Concurrent launch lock was ignored.' }
    Write-Output 'PASS: concurrent local launch rejected without replacing ownership state.'
}
finally { $launchLock.Dispose(); & (Join-Path $root 'scripts/Stop-Local.ps1') | Out-Null }
for ($round = 0; $round -lt 2; $round++) {
    $owned = $null
    try {
        $startup = [Diagnostics.Stopwatch]::StartNew()
        $owned = & (Join-Path $root 'scripts/Start-Local.ps1') -NoBuild:($round -gt 0) -PassThru
        if ($startup.Elapsed.TotalSeconds -gt 45) { throw 'Completed host build did not permit bounded startup; reusable compiler workers must not extend build lifetime.' }
        $health = Invoke-RestMethod 'http://127.0.0.1:5000/health'
        if ($health.status -ne 'ready') { throw 'Host failed health check.' }
        if (-not $owned.JoinLink.StartsWith('http://localhost:5000/#invite=')) { throw 'Bootstrap join link missing.' }
        $adminDenied = $false
        try { Invoke-RestMethod 'http://127.0.0.1:9997/v3/webrtc/sessions/list' | Out-Null }
        catch { $adminDenied = [int]$_.Exception.Response.StatusCode -eq 401 }
        if (-not $adminDenied) { throw 'Private media administration accepted an anonymous client.' }
        Write-Output "PASS: real local startup $($round + 1), private bootstrap available (invitation redacted)."
    }
    finally { if ($owned) { & (Join-Path $root 'scripts/Stop-Local.ps1') } }
    foreach ($item in $owned.Processes) {
        $process = Get-Process -Id $item.Pid -ErrorAction SilentlyContinue
        if ($process -and $process.StartTime.ToFileTimeUtc().ToString() -eq $item.CreationFileTime) { throw 'Owned service process remained after stop.' }
    }
    Write-Output "PASS: owned local services stopped after round $($round + 1)."
}
