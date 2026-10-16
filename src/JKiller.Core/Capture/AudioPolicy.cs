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
        var image = process?.ExecutablePath.Replace('/', '\\') ?? "";
        var name = image[(image.LastIndexOf('\\') + 1)..].ToLowerInvariant();
        var classification = name switch
        {
            "discord.exe" => ProcessClassification.Discord,
            "chrome.exe" or "msedge.exe" or "msedgewebview2.exe" or "firefox.exe" or "brave.exe" or "opera.exe" or "chromium.exe" => ProcessClassification.Browser,
            "geometrydash.exe" when image.EndsWith(@"\steamapps\common\Geometry Dash\GeometryDash.exe", StringComparison.OrdinalIgnoreCase) => ProcessClassification.VerifiedApplication,
            "capturefixture.exe" => ProcessClassification.VerifiedApplication,
            _ => ProcessClassification.Unknown
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
