using System.Text.Json;
using System.Text.Json.Nodes;
using JKiller.Windows.Obs;
namespace JKiller.Acceptance.Probes;
// Test-only capability injection; encoder preparation/output still use real OBS.
public sealed class CpuFallbackProbeRpc(IObsRpcClient inner) : IObsRpcClient, IAsyncDisposable
{
    public async Task<JsonElement> RequestAsync(string type, JsonElement data, CancellationToken token)
    {
        var result = await inner.RequestAsync(type, data, token);
        if (type != "CallVendorRequest" || !data.TryGetProperty("requestType", out var request) || request.GetString() != "GetCapabilities") return result;
        var json = JsonNode.Parse(result.GetRawText())!;
        var encoders = json["responseData"]!["encoders"]!.AsArray();
        foreach (var encoder in encoders.Where(e => e?["id"]?.GetValue<string>() is "obs_nvenc_h264_tex" or "h264_texture_amf").ToArray()) encoders.Remove(encoder);
        return JsonSerializer.SerializeToElement(json);
    }
    public ValueTask DisposeAsync() => inner is IAsyncDisposable disposable ? disposable.DisposeAsync() : ValueTask.CompletedTask;
}
