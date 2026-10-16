using JKiller.Contracts;
using System.Text.Json;
namespace JKiller.Core.Settings;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);
public sealed record SettingsParseResult(BroadcastSettings? Settings, ValidationResult Validation);
public static class StreamSettings
{
    public static SettingsParseResult ParseJson(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<BroadcastSettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (settings is null) return new(null, new(false, ["Streaming settings are required."]));
            var validation = Validate(settings);
            return new(validation.IsValid ? settings : null, validation);
        }
        catch (JsonException)
        {
            return new(null, new(false, ["Streaming settings must contain valid finite numeric values."]));
        }
    }
    public static ValidationResult Validate(BroadcastSettings settings)
    {
        List<string> errors = [];
        if ((settings.Width, settings.Height) is not ((854, 480) or (1280, 720) or (1920, 1080)))
            errors.Add("Choose 480p, 720p, or 1080p.");
        if (settings.Fps is not (30 or 60)) errors.Add("Choose 30 or 60 FPS.");
        if (settings.BitrateMbps is < 1m or > 25m || settings.BitrateMbps % 0.5m != 0)
            errors.Add("Choose 1–25 Mbps in 0.5 Mbps steps.");
        return new(errors.Count == 0, errors);
    }

    public static BroadcastSettings DefaultFor(int height, int fps)
    {
        if (fps is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(fps));
        return (height, fps) switch
        {
            (480, 30) => new(854, 480, 30, 2m),
            (480, 60) => new(854, 480, 60, 3m),
            (720, 30) => new(1280, 720, 30, 4m),
            (720, 60) => new(1280, 720, 60, 6m),
            (1080, 30) => new(1920, 1080, 30, 8m),
            (1080, 60) => new(1920, 1080, 60, 15m),
            _ => throw new ArgumentOutOfRangeException(nameof(height))
        };
    }
}
