using JKiller.Contracts;
namespace JKiller.Core.Capture;

public enum ProcessClassification { VerifiedApplication, Discord, Browser, SharedHost, OwnProcessTree, Unknown }
public sealed record AudioEligibility(bool Allowed, string Reason);
public static class AudioPolicy
{
    public static AudioEligibility Evaluate(SourceDescriptor source, ProcessClassification classification)
    {
        if (source.Process is not { Pid: > 0, CreationFileTime: > 0 }) return new(false, "Select a running application for audio; a display does not select audio.");
        return classification == ProcessClassification.VerifiedApplication
            ? new(true, "Only this application's process tree will be captured.")
            : new(false, classification switch
            {
                ProcessClassification.Discord => "Discord audio cannot be captured.",
                ProcessClassification.Browser or ProcessClassification.SharedHost => "Shared browser processes cannot safely isolate Discord or individual tabs. Audio is disabled.",
                ProcessClassification.OwnProcessTree => "Room playback cannot be captured; this prevents feedback.",
                _ => "This application's audio isolation has not been verified. Video remains available."
            });
    }

    public static bool IsSameProcess(ProcessIdentity? selected, ProcessIdentity? current) =>
        selected is { Pid: > 0, CreationFileTime: > 0 } && current is not null &&
        selected.Pid == current.Pid && selected.CreationFileTime == current.CreationFileTime &&
        !string.IsNullOrEmpty(selected.ExecutablePath) &&
        string.Equals(selected.ExecutablePath, current.ExecutablePath, StringComparison.OrdinalIgnoreCase);
}
