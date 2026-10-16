using JKiller.Contracts;
using System.Runtime.InteropServices;
using System.Text;
namespace JKiller.Windows.Capture;

public sealed record WindowObservation(ulong Handle, int Pid, string Title, bool Visible);
public sealed record MonitorObservation(ulong Handle, string Name);
public interface IWindowsSourceCatalog
{
    Task<IReadOnlyList<SourceDescriptor>> ListSourcesAsync(CancellationToken cancellationToken);
}
public sealed class WindowsSourceCatalog : IWindowsSourceCatalog
{
    private readonly Func<IReadOnlyList<WindowObservation>> _windows;
    private readonly Func<IReadOnlyList<MonitorObservation>> _monitors;
    private readonly Func<int, ProcessIdentity?> _readIdentity;
    public WindowsSourceCatalog() : this(ReadWindows, ReadMonitors, ProcessIdentityReader.Read) { }
    public WindowsSourceCatalog(Func<IReadOnlyList<WindowObservation>> windows, Func<IReadOnlyList<MonitorObservation>> monitors, Func<int, ProcessIdentity?> readIdentity)
        => (_windows, _monitors, _readIdentity) = (windows, monitors, readIdentity);

    public Task<IReadOnlyList<SourceDescriptor>> ListSourcesAsync(CancellationToken cancellationToken)
    {
        List<SourceDescriptor> sources = [];
        foreach (var monitor in _monitors())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sources.Add(new($"monitor:{monitor.Handle:x}", SourceKind.Monitor, monitor.Handle, null, monitor.Name));
        }
        foreach (var window in _windows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!window.Visible || window.Handle == 0) continue;
            var process = _readIdentity(window.Pid);
            sources.Add(new($"window:{window.Handle:x}:{window.Pid}:{process?.CreationFileTime ?? 0:x}", SourceKind.Window, window.Handle, process, window.Title));
        }
        return Task.FromResult<IReadOnlyList<SourceDescriptor>>(sources);
    }

    private static IReadOnlyList<WindowObservation> ReadWindows()
    {
        List<WindowObservation> windows = [];
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(Math.Max(256, GetWindowTextLength(hwnd) + 1));
            GetWindowText(hwnd, title, title.Capacity);
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0) windows.Add(new((ulong)hwnd, checked((int)pid), title.Length > 0 ? title.ToString() : "Untitled application", true));
            return true;
        }, 0);
        return windows;
    }

    private static IReadOnlyList<MonitorObservation> ReadMonitors()
    {
        List<MonitorObservation> monitors = [];
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref NativeRect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            var name = GetMonitorInfo(monitor, ref info) ? $"{info.Device} ({rect.Right - rect.Left}×{rect.Bottom - rect.Top})" : "Display";
            monitors.Add(new((ulong)monitor, name));
            return true;
        }, 0);
        return monitors;
    }

    private delegate bool EnumWindowsCallback(nint hwnd, nint data);
    private delegate bool EnumMonitorCallback(nint monitor, nint dc, ref NativeRect rect, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, EnumMonitorCallback callback, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
