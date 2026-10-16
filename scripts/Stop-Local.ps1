#requires -Version 7.4
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$state = Join-Path $root '.runtime/local-services.local.json'
if (-not (Test-Path -LiteralPath $state)) { Write-Output 'No tracked local JKiller services.'; return }
$owned = @(Get-Content -Raw -LiteralPath $state | ConvertFrom-Json)
if ($owned.Count -gt 2) { throw 'Unexpected local process inventory; refusing to stop arbitrary processes.' }
foreach ($entry in $owned) {
    $expected = switch ($entry.Role) { 'host' { Join-Path $root '.tools/dotnet/dotnet.exe' }; 'media' { Join-Path $root '.runtime/mediamtx/mediamtx.exe' }; default { throw 'Unknown owned process role.' } }
    if ([IO.Path]::GetFullPath($entry.ExecutablePath) -ne $expected) { throw 'Tracked executable path does not match the project-owned service.' }
    $process = Get-Process -Id $entry.Pid -ErrorAction SilentlyContinue
    if ($process) {
        if ($process.StartTime.ToFileTimeUtc().ToString() -ne $entry.CreationFileTime -or $process.MainModule.FileName -ne $expected) { throw 'PID was reused or executable identity changed; foreign process will not be stopped.' }
        $process.Kill(); $process.WaitForExit(5000) | Out-Null
        if (-not $process.HasExited) { throw 'Owned service did not stop within five seconds.' }
    }
}
Remove-Item -LiteralPath $state
Write-Output 'Stopped only tracked project-owned local services.'
