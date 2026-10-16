using JKiller.Contracts;
using System.Text.Json;
namespace JKiller.Windows.Obs;
public sealed class ObsDiagnosticsReader(Func<CancellationToken, Task<JsonElement>> read)
{
    public async Task<MediaDiagnostics> ReadAsync(CancellationToken token) =>
        (await read(token)).Deserialize<MediaDiagnostics>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Native diagnostics unavailable.");
}
