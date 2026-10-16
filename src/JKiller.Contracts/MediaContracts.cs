namespace JKiller.Contracts;

public sealed record BroadcastSettings(int Width, int Height, int Fps, decimal BitrateMbps)
{
    public int EncoderKbps => checked((int)(BitrateMbps * 1000m));
}
public sealed record ProcessIdentity(int Pid, ulong CreationFileTime, string ExecutablePath);
public enum SourceKind { Monitor, Window }
public sealed record SourceDescriptor(string Id, SourceKind Kind, ulong NativeHandle, ProcessIdentity? Process, string DisplayName);
public enum AudioMode { Off, Application, SystemOutput }
public sealed record AudioSelection(AudioMode Mode, ProcessIdentity? Application = null, string? RenderEndpointId = null)
{
    public static AudioSelection Off { get; } = new(AudioMode.Off);
}
public sealed record AudioOutputDescriptor(string Id, string DisplayName, bool IsDefault);
public sealed record BroadcastRequest(SourceDescriptor Video, AudioSelection Audio, BroadcastSettings Settings);
public sealed record AppliedMediaSettings(BroadcastSettings Settings, string Encoder, string Codec, bool HardwareVerified);
