param([switch]$SyntheticCapture)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools/nuget'
$personal = Join-Path $env:APPDATA 'obs-studio'
function Get-PersonalConfigHashes {
    $hashes = @{}
    if (Test-Path -LiteralPath $personal) {
        Get-ChildItem -LiteralPath $personal -File -Recurse | Where-Object { $_.Extension -in @('.ini','.json') } | ForEach-Object {
            $hashes[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
    return $hashes
}
$before = Get-PersonalConfigHashes
Push-Location $root
try {
    $options = @()
    if ($SyntheticCapture) { $options = @('--', 'capture') }
    & (Join-Path $root '.tools/dotnet/dotnet.exe') run --project tools/acceptance/OwnedEngineProbe @options
    if ($LASTEXITCODE -ne 0) { throw 'Actual owned-helper integration failed.' }
    $after = Get-PersonalConfigHashes
    if ($before.Count -ne $after.Count) { throw 'Personal OBS configuration file inventory changed during the probe.' }
    foreach ($path in $before.Keys) { if ($before[$path] -ne $after[$path]) { throw 'Personal OBS configuration changed during the probe.' } }
    Write-Output "PASS: $($before.Count) personal OBS configuration files remain byte-for-byte unchanged."
}
finally { Pop-Location }
