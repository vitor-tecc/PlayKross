using System.Text.Json;
using JKiller.Acceptance.Probes;
using JKiller.Windows.Obs;
using Xunit;
namespace JKiller.Windows.Tests;
public sealed class CpuFallbackProbeTests
{
    [Fact]
    public async Task InjectionSuppressesBothHardwareCandidatesLeavingSoftwareAndOpus()
    {
        await using var rpc = new CpuFallbackProbeRpc(new NativeReplies());
        var result = await rpc.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { requestType = "GetCapabilities" }), CancellationToken.None);
        var ids = result.GetProperty("responseData").GetProperty("encoders").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
        Assert.Equal(new[] { "obs_x264", "ffmpeg_opus" }, ids);
    }
    [Fact]
    public async Task ActualEncoderRepliesAreNotFabricatedByCapabilityInjection()
    {
        await using var rpc = new CpuFallbackProbeRpc(new NativeReplies());
        var result = await rpc.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { requestType = "PreparePipeline" }), CancellationToken.None);
        Assert.Equal("obs_x264", result.GetProperty("responseData").GetProperty("encoder").GetString());
        Assert.Equal(4000, result.GetProperty("responseData").GetProperty("bitrateKbps").GetInt32());
    }
    private sealed class NativeReplies : IObsRpcClient
    {
        public Task<JsonElement> RequestAsync(string type, JsonElement data, CancellationToken token) => Task.FromResult(data.GetProperty("requestType").GetString() == "GetCapabilities"
            ? JsonSerializer.SerializeToElement(new { responseData = new { encoders = new[] { new { id = "obs_nvenc_h264_tex" }, new { id = "h264_texture_amf" }, new { id = "obs_x264" }, new { id = "ffmpeg_opus" } } } })
            : JsonSerializer.SerializeToElement(new { responseData = new { encoder = "obs_x264", bitrateKbps = 4000 } }));
    }
}
