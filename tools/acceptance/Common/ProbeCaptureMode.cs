using JKiller.Contracts;
namespace JKiller.Acceptance.Probes;
public static class ProbeCaptureMode
{
    public static SourceDescriptor? SelectAudio(string? mode, SourceDescriptor selected) => mode switch
    {
        null or "" or "application" => selected,
        "video-only" => null,
        _ => throw new ArgumentException("Use application or video-only probe capture mode.", nameof(mode))
    };
}
