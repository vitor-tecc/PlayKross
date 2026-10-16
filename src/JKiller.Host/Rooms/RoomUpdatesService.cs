using JKiller.Contracts;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
namespace JKiller.Host.Rooms;
public sealed class RoomUpdatesService(RoomStore rooms, RoomConnections connections, IHubContext<RoomHub> hub, ILogger<RoomUpdatesService> logger) : BackgroundService
{
    // At most one pending snapshot per room; room count is bounded by RoomStore.
    private readonly ConcurrentDictionary<Guid, RoomSnapshot> pending = new();
    private void Changed(RoomSnapshot snapshot) => pending.AddOrUpdate(snapshot.RoomId, snapshot, (_, previous) => previous.Revision > snapshot.Revision ? previous : snapshot);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        rooms.Changed += Changed;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                connections.Sweep();
                foreach (var id in pending.Keys)
                    if (pending.TryRemove(id, out var snapshot))
                        try { await connections.SendAsync(hub, snapshot, stoppingToken); }
                        catch (Exception error) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(error, "Room snapshot delivery failed."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { rooms.Changed -= Changed; }
    }
}
