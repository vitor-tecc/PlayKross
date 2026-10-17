using JKiller.Contracts;
namespace JKiller.Acceptance.Probes;
public static class ProbeCaptureMode
{
    public static AudioSelection SelectAudio(string? mode, SourceDescriptor selected, string? renderEndpointId = null) => mode switch
    {
        null or "" or "application" => new(AudioMode.Application, selected.Process),
        "video-only" => AudioSelection.Off,
        "system-output" when !string.IsNullOrWhiteSpace(renderEndpointId) && !string.Equals(renderEndpointId.Trim(), "default", StringComparison.OrdinalIgnoreCase) => new(AudioMode.SystemOutput, RenderEndpointId: renderEndpointId),
        _ => throw new ArgumentException("Use application, video-only, or system-output with a concrete render endpoint ID.", nameof(mode))
    };
}
