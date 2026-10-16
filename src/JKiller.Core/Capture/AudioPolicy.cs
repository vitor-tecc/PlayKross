using JKiller.Contracts;
namespace JKiller.Core.Capture;

public enum ProcessClassification { VerifiedApplication, Discord, Browser, SharedHost, OwnProcessTree, Unknown }
public sealed record AudioEligibility(bool Allowed, string Reason);
public static class AudioPolicy
{
    public static AudioEligibility EvaluateSelection(AudioSelection audio, SourceKind videoKind, int ownerPid = 0)
    {
        switch (audio.Mode)
        {
            case AudioMode.Off:
                return audio.Application is null && audio.RenderEndpointId is null
                    ? new(true, "Audio is off.")
                    : new(false, "Audio off cannot include an application or output device.");
            case AudioMode.Application:
                return audio.RenderEndpointId is null
                    ? EvaluateApplication(audio.Application, ownerPid)
                    : new(false, "Application audio cannot include an output device.");
            case AudioMode.SystemOutput:
                if (audio.Application is not null || string.IsNullOrWhiteSpace(audio.RenderEndpointId) ||
                    string.Equals(audio.RenderEndpointId.Trim(), "default", StringComparison.OrdinalIgnoreCase))
                    return new(false, "Select a concrete output device for system audio, without an application.");
                return videoKind == SourceKind.Monitor
                    ? new(true, "Captures this output's mixed sound, including Discord and incoming room playback. Avoid feedback.")
                    : new(false, "System output audio requires sharing a display.");
            default:
                return new(false, "Unknown audio mode.");
        }
    }

    public static AudioEligibility Evaluate(SourceDescriptor source, ProcessClassification classification)
    {
        if (source.Process is not { Pid: > 0, CreationFileTime: > 0 } || string.IsNullOrWhiteSpace(source.Process.ExecutablePath))
            return new(false, "Select a running application with an exact process identity for audio; a display does not select audio.");
        return classification == ProcessClassification.VerifiedApplication
            ? new(true, "Experimental: captures this app and its child processes. Future children cannot be guaranteed excluded; keep Discord and room playback outside this app's process tree.")
            : new(false, classification switch
            {
                ProcessClassification.Discord => "Discord audio cannot be captured.",
                ProcessClassification.Browser or ProcessClassification.SharedHost => "Shared browser processes cannot safely isolate Discord or individual tabs. Audio is disabled.",
                ProcessClassification.OwnProcessTree => "Room playback cannot be captured; this prevents feedback.",
                _ => "This application's audio isolation has not been verified. Video remains available."
            });
    }

    public static AudioEligibility EvaluateApplication(ProcessIdentity? process, int ownerPid = 0)
    {
        var image = process?.ExecutablePath?.Replace('/', '\\') ?? "";
        var name = image[(image.LastIndexOf('\\') + 1)..].ToLowerInvariant();
        // Match the independent native ForbiddenImage exclusions; eligibility alone does
        // not establish liveness, owner-tree membership or child-process isolation.
        var classification = name switch
        {
            _ when name.Contains("discord", StringComparison.Ordinal) => ProcessClassification.Discord,
            _ when name.Contains("jkiller", StringComparison.Ordinal) || name.Contains("playkross", StringComparison.Ordinal) => ProcessClassification.OwnProcessTree,
            "obs64.exe" => ProcessClassification.OwnProcessTree,
            "chrome.exe" or "msedge.exe" or "msedgewebview2.exe" or "firefox.exe" or "brave.exe" or "opera.exe" or "chromium.exe" => ProcessClassification.Browser,
            "steamwebhelper.exe" or "explorer.exe" or "rundll32.exe" or "dllhost.exe" => ProcessClassification.SharedHost,
            _ => ProcessClassification.VerifiedApplication
        };
        if (process is not null && ownerPid > 0 && process.Pid == ownerPid) classification = ProcessClassification.OwnProcessTree;
        return Evaluate(new("audio", SourceKind.Window, 0, process, "Selected application"), classification);
    }

    public static bool IsSameProcess(ProcessIdentity? selected, ProcessIdentity? current) =>
        selected is { Pid: > 0, CreationFileTime: > 0 } && current is not null &&
        selected.Pid == current.Pid && selected.CreationFileTime == current.CreationFileTime &&
        !string.IsNullOrEmpty(selected.ExecutablePath) &&
        string.Equals(selected.ExecutablePath, current.ExecutablePath, StringComparison.OrdinalIgnoreCase);
}
