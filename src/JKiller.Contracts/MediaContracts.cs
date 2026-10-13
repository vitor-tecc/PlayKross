namespace JKiller.Contracts;

public sealed record BroadcastSettings(int Width, int Height, int Fps, decimal BitrateMbps)
{
    public int EncoderKbps => checked((int)(BitrateMbps * 1000m));
}
public sealed record ProcessIdentity(int Pid, ulong CreationFileTime, string ExecutablePath);
public enum SourceKind { Monitor, Window }
public sealed record SourceDescriptor(string Id, SourceKind Kind, ulong NativeHandle, ProcessIdentity? Process, string DisplayName);
public sealed record AudioSelection(ProcessIdentity? Application);
public sealed record BroadcastRequest(SourceDescriptor Video, AudioSelection Audio, BroadcastSettings Settings);
public sealed record AppliedMediaSettings(BroadcastSettings Settings, string Encoder, string Codec, bool HardwareVerified);
