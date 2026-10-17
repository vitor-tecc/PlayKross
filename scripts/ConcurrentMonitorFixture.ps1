# Acceptance-only. Dot-sourcing defines functions; it does not load user32 or activate devices.
function Assert-ConcurrentMonitorSurface($Owner,$Observation,[int[]]$Bounds) {
    if($Observation.Id -ne $Owner.Id -or $Observation.Creation -ne $Owner.Creation -or $Observation.Path -ne $Owner.Path -or $Observation.WindowPid -ne $Owner.Id -or -not $Observation.Visible -or -not $Observation.Topmost -or -not $Observation.Borderless -or ($Observation.Bounds -join ',') -ne ($Bounds -join ',')){
        throw ('Owned synthetic monitor surface verification failed; capture refused. '+(@{expectedPid=$Owner.Id;actualPid=$Observation.Id;windowPid=$Observation.WindowPid;creationMatches=($Owner.Creation -eq $Observation.Creation);pathMatches=($Owner.Path -eq $Observation.Path);expectedBounds=$Bounds;actualBounds=$Observation.Bounds;visible=$Observation.Visible;topmost=$Observation.Topmost;borderless=$Observation.Borderless}|ConvertTo-Json -Compress))
    }
}
function Set-ConcurrentMonitorFixture($Owner,[long]$MonitorHandle) {
    if(-not ('ConcurrentMonitorFixtureNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ConcurrentMonitorFixtureNative {
    [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom; public int[] Values()=>new[]{Left,Top,Right,Bottom};}
    [StructLayout(LayoutKind.Sequential)] struct Info {public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll",SetLastError=true)] static extern bool GetMonitorInfoW(IntPtr monitor,ref Info info);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowLongPtrW(IntPtr window,int index,IntPtr value);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtrW(IntPtr window,int index);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static Rect Cover(IntPtr window,IntPtr monitor,int owner) {
        var dpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
        GetWindowThreadProcessId(window,out var pid);if(pid!=owner)throw new InvalidOperationException("Foreign fixture window refused.");
        var info=new Info{Size=Marshal.SizeOf<Info>()};if(!GetMonitorInfoW(monitor,ref info))throw new InvalidOperationException("Selected monitor disappeared.");
        long style=(GetWindowLongPtrW(window,-16).ToInt64() & ~0x00cf0000L) | 0x80000000L;
        SetWindowLongPtrW(window,-16,new IntPtr(style));var r=info.Monitor;
        if(!SetWindowPos(window,new IntPtr(-1),r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top,0x70))throw new InvalidOperationException("Cannot cover selected monitor with owned fixture.");
        return r;
        } finally {if(dpi!=IntPtr.Zero)SetThreadDpiAwarenessContext(dpi);}
    }
    public static object Observe(IntPtr window) {
        var dpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
        GetWindowThreadProcessId(window,out var pid);if(!GetWindowRect(window,out var r))throw new InvalidOperationException("Fixture geometry unavailable.");
        return new {WindowPid=(int)pid,Bounds=r.Values(),Visible=IsWindowVisible(window),Topmost=(GetWindowLongPtrW(window,-20).ToInt64()&8)!=0,Borderless=(GetWindowLongPtrW(window,-16).ToInt64()&0x00cf0000L)==0};
        } finally {if(dpi!=IntPtr.Zero)SetThreadDpiAwarenessContext(dpi);}
    }
}
'@
    }
    $process=Get-Process -Id $Owner.Id; $process.Refresh()
    if($process.StartTime.ToFileTimeUtc().ToString() -ne $Owner.Creation -or $process.MainModule.FileName -ne $Owner.Path -or $process.ProcessName -ne 'CaptureFixture'){throw 'Foreign or replaced process refused for synthetic monitor coverage.'}
    $bounds=[ConcurrentMonitorFixtureNative]::Cover($process.MainWindowHandle,[IntPtr]::new($MonitorHandle),$Owner.Id).Values()
    Start-Sleep -Milliseconds 150
    $process.Refresh();$native=[ConcurrentMonitorFixtureNative]::Observe($process.MainWindowHandle)
    $observation=@{Id=$process.Id;Creation=$process.StartTime.ToFileTimeUtc().ToString();Path=$process.MainModule.FileName;WindowPid=$native.WindowPid;Bounds=$native.Bounds;Visible=$native.Visible;Topmost=$native.Topmost;Borderless=$native.Borderless}
    Assert-ConcurrentMonitorSurface $Owner $observation $bounds
    return $observation
}
