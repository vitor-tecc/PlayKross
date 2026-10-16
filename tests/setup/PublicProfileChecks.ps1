#requires -Version 7.4
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$old=$env:JKILLER_PUBLIC_HOST; $services=$null; $publicMedia=$null
try {
    $env:JKILLER_PUBLIC_HOST='203.0.113.10'
    $adapted=& (Join-Path $root '.tools/caddy/caddy.exe') adapt --config (Join-Path $root 'config/Caddyfile.example') --adapter caddyfile
    if ($LASTEXITCODE -ne 0) { throw 'Caddy public example failed parsing.' }
    $json=$adapted | ConvertFrom-Json
    if ($json.admin.listen -ne '127.0.0.1:2019' -or $json.apps.tls.automation.policies[0].issuers[0].profile -ne 'shortlived') { throw 'Caddy admin/certificate profile changed.' }
    Write-Output 'PASS: Caddy 2.11.7 public-IP profile parsed with private administration, without starting HTTPS/certificate issuance.'
    $services=& (Join-Path $root 'scripts/Start-Local.ps1') -NoBuild -PassThru
    $entry=$services.Processes | Where-Object Role -eq media
    $media=Get-Process -Id $entry.Pid
    if ($media.StartTime.ToFileTimeUtc().ToString() -ne $entry.CreationFileTime -or $media.Path -ne $entry.ExecutablePath) { throw 'Owned media identity changed.' }
    $media.Kill(); $media.WaitForExit(5000) | Out-Null
    # Override only the public media listener to loopback during configuration validation.
    $directory=Join-Path $root '.artifacts/deployment'; New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $publicMedia=Start-Process -FilePath $entry.ExecutablePath -ArgumentList ('"'+(Join-Path $root 'config/mediamtx.public.yml')+'"') -Environment @{MTX_WEBRTCLOCALUDPADDRESS='127.0.0.1:8189'} -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $directory 'media.log') -RedirectStandardError (Join-Path $directory 'media-errors.log') -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(10); $denied=$false
    do {
        if ($publicMedia.HasExited) { throw 'MediaMTX public profile failed parsing; inspect deployment logs.' }
        try { Invoke-RestMethod 'http://127.0.0.1:9997/v3/webrtc/sessions/list' -TimeoutSec 1 | Out-Null } catch { $denied=$_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401 }
        if (-not $denied) { Start-Sleep -Milliseconds 100 }
    } until ($denied -or [DateTime]::UtcNow -ge $deadline)
    if (-not $denied) { throw 'Anonymous API access was not rejected.' }
    $udp=Get-NetUDPEndpoint -OwningProcess $publicMedia.Id | Where-Object LocalPort -eq 8189
    if (-not $udp -or @($udp | Where-Object LocalAddress -ne '127.0.0.1').Count -gt 0) { throw 'Configuration validation must not expose public UDP.' }
    Write-Output 'PASS: actual MediaMTX parsed public YAML, retained authenticated private API, and used loopback-only UDP override for this test.'
} finally {
    $env:JKILLER_PUBLIC_HOST=$old
    if ($publicMedia -and -not $publicMedia.HasExited) { $publicMedia.Kill(); $publicMedia.WaitForExit(5000) | Out-Null }
    if ($services) { & (Join-Path $root 'scripts/Stop-Local.ps1') }
}
