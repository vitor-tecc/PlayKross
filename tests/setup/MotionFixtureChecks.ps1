#requires -Version 7.4
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not ('JKMotionNative' -as [type])) { Add-Type 'using System; using System.Runtime.InteropServices; public static class JKMotionNative { [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr hwnd, string name); }' }
$fixture=Start-Process -FilePath (Join-Path $root '.artifacts/native/Debug/CaptureFixture.exe') -ArgumentList '--timing' -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 1; $fixture.Refresh(); $window=$fixture.MainWindowHandle
    if (-not $window) { throw 'Synthetic timing window unavailable.' }
    $start=[JKMotionNative]::GetPropW($window,'JKFixturePaintCount').ToInt64(); $clock=[Diagnostics.Stopwatch]::StartNew(); Start-Sleep -Seconds 5
    $finish=[JKMotionNative]::GetPropW($window,'JKFixturePaintCount').ToInt64(); $fps=($finish-$start)/$clock.Elapsed.TotalSeconds
    Write-Output ('Measured synthetic paint FPS: '+$fps.ToString('F2'))
    if ($fps -lt 57 -or $fps -gt 63) { throw 'Synthetic motion source does not meet its measured 60 FPS cadence.' }
} finally { if (-not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit(2000) | Out-Null } }
