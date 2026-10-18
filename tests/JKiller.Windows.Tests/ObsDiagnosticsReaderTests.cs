using System.Text.Json;
using JKiller.Windows.Obs;
using Xunit;
namespace JKiller.Windows.Tests;
public sealed class ObsDiagnosticsReaderTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OptionalAmfMetadataRemainsCompatibleWithLegacyDiagnostics(bool amf)
    {
        var payload = amf ? "{\"encoder\":\"h264_texture_amf\",\"encoderFamily\":\"amf\",\"encoderTransferRoute\":\"unverified\",\"hardwareInitialized\":true}" : "{\"encoder\":\"obs_nvenc_h264_tex\",\"hardwareInitialized\":true}";
        var result = await new ObsDiagnosticsReader(_ => Task.FromResult(JsonDocument.Parse(payload).RootElement.Clone())).ReadAsync(CancellationToken.None);
        // Inspect serialized output to keep this test executable against the old
        // contract: the new fields must survive the actual reader boundary.
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (amf) { Assert.Equal("amf", json.GetProperty("encoderFamily").GetString()); Assert.Equal("unverified", json.GetProperty("encoderTransferRoute").GetString()); }
        else Assert.True(result.HardwareInitialized);
    }
}
