using JKiller.Contracts;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
namespace JKiller.Windows.Capture;
public static class ProcessIdentityReader
{
    public static ProcessIdentity? Read(int pid)
    {
        if (pid <= 0) return null;
        using var handle = OpenProcess(0x1000 | 0x100000, false, (uint)pid);
        if (handle.IsInvalid || WaitForSingleObject(handle, 0) != 258) return null;
        if (!GetProcessTimes(handle, out var created, out _, out _, out _)) return null;
        var image = new StringBuilder(32768);
        var length = image.Capacity;
        if (!QueryFullProcessImageName(handle, 0, image, ref length) || WaitForSingleObject(handle, 0) != 258) return null;
        return new(pid, created, image.ToString());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle handle, out ulong created, out ulong exited, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, StringBuilder name, ref int size);
}
