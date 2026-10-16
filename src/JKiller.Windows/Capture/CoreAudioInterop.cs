using System.Runtime.InteropServices;
namespace JKiller.Windows.Capture;

// ABI verified against Windows SDK 10.0.26100.0 mmdeviceapi.h, propsys.h,
// propidlbase.h and functiondiscoverykeys_devpkey.h. Enumeration never activates audio.
internal static class CoreAudioInterop
{
    private const int Render = 0, ConsoleRole = 0;
    private const uint DeviceStateActive = 1;
    private const int NotFound = unchecked((int)0x80070490), ChangedApartment = unchecked((int)0x80010106);
    private static readonly PropertyKey FriendlyName = new(new("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    internal static AudioEndpointSnapshot ReadActiveRenderEndpoints(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var initialized = CoInitializeEx(0, 0); // MTA, or use the caller's existing apartment.
        if (initialized != ChangedApartment) Marshal.ThrowExceptionForHR(initialized);
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? devices = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var defaultId = ReadDefaultId(enumerator);
            token.ThrowIfCancellationRequested();
            enumerator.EnumAudioEndpoints(Render, DeviceStateActive, out devices);
            devices.GetCount(out var count);
            List<AudioEndpointObservation> endpoints = [];
            for (uint index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                IMMDevice? device = null;
                try
                {
                    devices.Item(index, out device);
                    device.GetState(out var state);
                    if (state != DeviceStateActive) continue;
                    var id = ReadId(device);
                    endpoints.Add(new(id, ReadFriendlyName(device), AudioEndpointFlow.Render, AudioEndpointState.Active));
                }
                finally { Release(device); }
            }
            token.ThrowIfCancellationRequested();
            return new(endpoints, defaultId);
        }
        finally
        {
            Release(devices);
            Release(enumerator);
            if (initialized >= 0) CoUninitialize();
        }
    }

    private static string? ReadDefaultId(IMMDeviceEnumerator enumerator)
    {
        IMMDevice? device = null;
        try
        {
            // Resolve once to a concrete ID. Later default switches never retarget a selection.
            var result = enumerator.GetDefaultAudioEndpoint(Render, ConsoleRole, out device);
            if (result == NotFound) return null;
            Marshal.ThrowExceptionForHR(result);
            return ReadId(device!);
        }
        finally { Release(device); }
    }

    private static string ReadId(IMMDevice device)
    {
        device.GetId(out var id);
        try { return Marshal.PtrToStringUni(id) ?? throw new InvalidOperationException("Audio endpoint has no ID."); }
        finally { Marshal.FreeCoTaskMem(id); }
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? properties = null;
        PropVariant name = default;
        try
        {
            device.OpenPropertyStore(0, out properties); // STGM_READ
            var key = FriendlyName;
            properties.GetValue(ref key, out name);
            return name.VariantType == 31 ? Marshal.PtrToStringUni(name.Value.Pointer) : null; // VT_LPWSTR
        }
        finally
        {
            PropVariantClear(ref name);
            Release(properties);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null) Marshal.ReleaseComObject(value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    // A PROPVARIANT includes counted-array union members: 24 bytes on x64, 16 on x86.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VariantType, Reserved1, Reserved2, Reserved3;
        public PropVariantValue Value;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariantValue
    {
        [FieldOffset(0)] public nint Pointer;
        [FieldOffset(0)] public CountedArray Array;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CountedArray { public uint Count; public nint Pointer; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice? endpoint);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(nint client);
        void UnregisterEndpointNotificationCallback(nint client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid interfaceId, uint context, nint activationParameters, out nint instance);
        void OpenPropertyStore(uint access, out IPropertyStore properties);
        void GetId(out nint id);
        void GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}
