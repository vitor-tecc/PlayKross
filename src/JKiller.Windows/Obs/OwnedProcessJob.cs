using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace JKiller.Windows.Obs;

internal sealed class OwnedProcessJob(Process process, SafeFileHandle job) : IDisposable
{
    public Process Process { get; } = process;
    public void Dispose() => job.Dispose();

    public static OwnedProcessJob Launch(ProcessStartInfo launch)
    {
        // Assignment is atomic with process creation, including before OBS initializes.
        // A non-inheritable job handle belongs only to this controller. Windows closes it
        // on controller exit and terminates every owned helper descendant.
        var job = new SafeFileHandle(CreateJobObjectW(0, null), true);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint attributes = 0, jobList = 0, environment = 0;
        bool initialized = false;
        try
        {
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>())) throw new Win32Exception(Marshal.GetLastWin32Error());
            nuint size = 0; InitializeProcThreadAttributeList(0, 1, 0, ref size);
            if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            attributes = Marshal.AllocHGlobal(checked((nint)size));
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            jobList = Marshal.AllocHGlobal(nint.Size); Marshal.WriteIntPtr(jobList, job.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, 0x0002000D, jobList, (nuint)nint.Size, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var block = string.Join('\0', launch.Environment.OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase).Select(value => value.Key + "=" + value.Value)) + "\0\0";
            environment = Marshal.StringToHGlobalUni(block);
            var startup = new StartupInfoEx { Startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>(), Flags = 1, ShowWindow = 0 }, Attributes = attributes };
            var command = new StringBuilder(string.Join(' ', new[] { launch.FileName }.Concat(launch.ArgumentList).Select(Quote)));
            const uint flags = 0x00000400 | 0x00080000 | 0x08000000; // UNICODE_ENVIRONMENT, EXTENDED_STARTUPINFO, NO_WINDOW
            if (!CreateProcessW(launch.FileName, command, 0, 0, false, flags, environment, launch.WorkingDirectory, in startup, out var created)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return new OwnedProcessJob(Process.GetProcessById((int)created.ProcessId), job); }
            finally { CloseHandle(created.Thread); CloseHandle(created.Process); }
        }
        catch { job.Dispose(); throw; }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != 0) Marshal.FreeHGlobal(attributes);
            if (jobList != 0) Marshal.FreeHGlobal(jobList);
            if (environment != 0) Marshal.FreeHGlobal(environment);
        }
    }
    private static string Quote(string argument)
    {
        var quoted = new StringBuilder("\""); int slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') { quoted.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
            quoted.Append('\\', slashes).Append(character); slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public nuint MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo { public uint Size; public nint Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XChars, YChars, FillAttribute, Flags; public ushort ShowWindow, ReservedSize; public nint ReservedData, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Startup; public nint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, in ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessW(string application, StringBuilder command, nint processSecurity, nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment, string directory, in StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
