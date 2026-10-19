#requires -Version 7.4
param([string]$MediaInterface)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$services = $null
$previousLink = $env:JKILLER_PROBE_JOIN_LINK
$previousMode = $env:JKILLER_BROWSER_PUBLISHING
try {
    Push-Location $root
    try {
        npm --prefix web run build
        if ($LASTEXITCODE -ne 0) { throw 'Browser production build failed.' }
        $services = & (Join-Path $PSScriptRoot 'Start-Local.ps1') -MediaInterface $MediaInterface -PassThru
        $env:JKILLER_PROBE_JOIN_LINK = $services.JoinLink
        $env:JKILLER_BROWSER_PUBLISHING = '1'
        npm --prefix web run test:protocol -- browser-publishing.spec.ts
        if ($LASTEXITCODE -ne 0) { throw 'Real browser publication/playback regression failed.' }
    } finally { Pop-Location }
} finally {
    $env:JKILLER_PROBE_JOIN_LINK = $previousLink
    $env:JKILLER_BROWSER_PUBLISHING = $previousMode
    if ($services) { & (Join-Path $PSScriptRoot 'Stop-Local.ps1') }
}
