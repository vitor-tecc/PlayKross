using JKiller.Contracts;
using JKiller.Core.Diagnostics;
using JKiller.Host.Rooms;
using JKiller.Host.Security;
namespace JKiller.Host.Diagnostics;
public sealed class DiagnosticStore(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly LinkedList<(Guid Room, Guid Session, DateTimeOffset At, DiagnosticsEnvelope Sample)> samples = new();
    public bool Add(Guid room, Guid session, DiagnosticsEnvelope envelope)
    {
        lock (gate)
        {
            Purge(); var now = clock.GetUtcNow();
            if (samples.Any(s => s.Session == session && s.Sample.StreamId == envelope.StreamId && now - s.At < TimeSpan.FromMilliseconds(900))) return false;
            var clean = DiagnosticsExport.Redact([envelope with { ObserverId = session }]).Samples.Single();
            var metrics = clean.Metrics.ToDictionary(); metrics["serverReceivedUnixMs"] = now.ToUnixTimeMilliseconds();
            samples.AddLast((room, session, now, clean with { Metrics = metrics }));
            while (samples.Count > 4096) samples.RemoveFirst(); return true;
        }
    }
    public DiagnosticsEnvelope[] Read(RoomSnapshot room)
    {
        lock (gate)
        {
            Purge(); var sessions = room.Participants.Select(p => p.SessionId).ToHashSet(); var streams = room.Streams.Select(s => s.StreamId).ToHashSet();
            return samples.Where(s => s.Room == room.RoomId && sessions.Contains(s.Session) && streams.Contains(s.Sample.StreamId)).Select(s => s.Sample).ToArray();
        }
    }
    private void Purge() { while (samples.First is { } first && clock.GetUtcNow() - first.Value.At > TimeSpan.FromMinutes(2)) samples.RemoveFirst(); }
}
public static class DiagnosticsEndpoints
{
    public static void MapDiagnostics(this WebApplication app)
    {
        app.MapPost("/api/diagnostics", (DiagnosticsEnvelope envelope, HttpContext context, RoomStore rooms, DiagnosticStore store) =>
        {
            if (envelope.Role is not ("publisher" or "viewer") || envelope.Metrics is null || envelope.Metadata is null || envelope.Metrics.Count > 64 || envelope.Metadata.Count > 16) return Results.BadRequest();
            var identity = SessionAuthenticationHandler.Require(context); var room = rooms.Snapshot(identity.SessionId);
            var stream = room.Streams.SingleOrDefault(s => s.StreamId == envelope.StreamId);
            if (stream is null || envelope.Role == "publisher" && stream.OwnerSessionId != identity.SessionId) return Results.Forbid();
            return store.Add(room.RoomId, identity.SessionId, envelope) ? Results.Ok(new { accepted = true }) : Results.StatusCode(429);
        }).RequireAuthorization();
        app.MapGet("/api/diagnostics", async (HttpContext context, RoomStore rooms, DiagnosticStore store, IHostTrafficReader traffic) =>
        {
            var room = rooms.Snapshot(SessionAuthenticationHandler.Require(context).SessionId);
            var snapshot = await traffic.ReadAsync(room, context.RequestAborted);
            var host = snapshot.Aggregate;
            var samples = store.Read(room);
            var hostMetrics = host.Metrics.ToDictionary();
            hostMetrics["estimatedViewerMbps"] = HostBandwidthEstimate.EstimateMbps(room, snapshot.ReaderCounts, samples);
            var metadata = host.Metadata.ToDictionary(); metadata["estimateBasis"] = "video-payload-only";
            return Results.Ok(DiagnosticsExport.Redact(samples.Append(host with { Metrics = hostMetrics, Metadata = metadata })));
        }).RequireAuthorization();
    }
}
