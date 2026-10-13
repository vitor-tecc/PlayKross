using JKiller.Contracts;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
namespace JKiller.Host.Diagnostics;
public interface IHostTrafficReader { Task<DiagnosticsEnvelope> ReadAsync(RoomSnapshot room, CancellationToken token); }
// Byte counters are cumulative for currently live sessions, not lifetime totals or NIC attribution.
public sealed class HostTrafficReader : IHostTrafficReader, IDisposable
{
    private readonly HttpClient client;
    private readonly SemaphoreSlim gate = new(1);
    private DateTimeOffset refreshed;
    private sealed record SessionCounter(string Id, string Path, string State, double Sent, double Received, double? SendMbps, double? ReceiveMbps);
    private List<SessionCounter>? cache;
    public HostTrafficReader(HostOptions options)
    {
        client = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = options.AdminOrigin, Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 262144 };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("jk-admin:" + options.AdminSecret)));
    }
    public async Task<DiagnosticsEnvelope> ReadAsync(RoomSnapshot room, CancellationToken token)
    {
        var metrics = new Dictionary<string, double?> { ["mediaBytesSent"] = null, ["mediaBytesReceived"] = null, ["activeMediaSessions"] = null, ["estimatedViewerMbps"] = null, ["hostNicBytesSent"] = null, ["hostNicBytesReceived"] = null };
        await gate.WaitAsync(token);
        try
        {
            if (DateTimeOffset.UtcNow - refreshed > TimeSpan.FromSeconds(3))
            {
                var previous = cache; cache = null; var interval = (DateTimeOffset.UtcNow - refreshed).TotalSeconds;
                try
                {
                    var next = new List<SessionCounter>(); int pages = 1;
                    for (int page = 0; page < pages; page++)
                    {
                        using var response = await client.GetAsync($"/v3/webrtc/sessions/list?page={page}&itemsPerPage=100", token); response.EnsureSuccessStatusCode();
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
                        pages = doc.RootElement.GetProperty("pageCount").GetInt32(); if (pages > 4) throw new InvalidDataException("Session inventory limit exceeded.");
                        foreach (var s in doc.RootElement.GetProperty("items").EnumerateArray())
                        {
                            var id = s.GetProperty("id").GetString()!; var sent = s.GetProperty("outboundBytes").GetDouble(); var received = s.GetProperty("inboundBytes").GetDouble();
                            var prior = previous?.SingleOrDefault(p => p.Id == id);
                            double? Rate(double? old, double value) => old is not null && value >= old && interval > 0 ? (value - old.Value) * 8 / 1e6 / interval : null;
                            next.Add(new(id, s.GetProperty("path").GetString()!, s.GetProperty("state").GetString()!, sent, received, Rate(prior?.Sent, sent), Rate(prior?.Received, received)));
                        }
                    }
                    cache = next;
                }
                catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException or KeyNotFoundException || e is OperationCanceledException && !token.IsCancellationRequested) { }
                refreshed = DateTimeOffset.UtcNow;
            }
            if (cache is not null)
            {
                var paths = room.Streams.Select(s => $"rooms/{room.RoomId:N}/streams/{s.StreamId:N}/main").ToHashSet();
                var selected = cache.Where(s => paths.Contains(s.Path)).ToArray();
                metrics["mediaBytesSent"] = selected.Sum(s => s.Sent); metrics["mediaBytesReceived"] = selected.Sum(s => s.Received); metrics["activeMediaSessions"] = selected.Length;
                metrics["activeReaders"] = selected.Count(s => s.State == "read");
                metrics["mediaSendMbps"] = selected.All(s => s.SendMbps is not null) ? selected.Sum(s => s.SendMbps) : null;
                metrics["mediaReceiveMbps"] = selected.All(s => s.ReceiveMbps is not null) ? selected.Sum(s => s.ReceiveMbps) : null;
            }
        }
        finally { gate.Release(); }
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.OperationalStatus == OperationalStatus.Up).Select(n => n.GetIPStatistics()).ToArray();
            metrics["hostNicBytesSent"] = nics.Sum(s => (double)s.BytesSent); metrics["hostNicBytesReceived"] = nics.Sum(s => (double)s.BytesReceived);
        }
        catch (NetworkInformationException) { }
        return new(Guid.Empty, "host", metrics, new Dictionary<string, string> { ["provenance"] = "mediamtx-api-and-all-active-nics" });
    }
    public void Dispose() { client.Dispose(); gate.Dispose(); }
}
