using JKiller.Host.Rooms;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;
namespace JKiller.Host.Media;
public sealed class MediaRevocationService : BackgroundService
{
    private readonly RoomStore rooms;
    private readonly MediaResourceStore resources;
    private readonly MediaSessionTracker tracker;
    private readonly IMediaControl control;
    private readonly ILogger<MediaRevocationService> logger;
    private readonly Channel<(string? Path, Guid? Owner)> pending = Channel.CreateBounded<(string?, Guid?)>(4096);
    public MediaRevocationService(RoomStore rooms, MediaResourceStore resources, MediaSessionTracker tracker, IMediaControl control, ILogger<MediaRevocationService> logger)
    {
        this.rooms = rooms; this.resources = resources; this.tracker = tracker; this.control = control; this.logger = logger;
        rooms.StreamRevoked += PathRevoked; rooms.SessionRevoked += OwnerRevoked;
    }
    private void PathRevoked(string path) { resources.RevokePath(path); if (!pending.Writer.TryWrite((path, null))) throw new InvalidOperationException("Media revocation queue is full."); }
    private void OwnerRevoked(Guid owner) { resources.RevokeOwner(owner); if (!pending.Writer.TryWrite((null, owner))) throw new InvalidOperationException("Media revocation queue is full."); }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var sweep = Sweep(timer, stoppingToken);
        try
        {
            await foreach (var item in pending.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (item.Path is { } path)
                    {
                        foreach (var session in tracker.ForPath(path)) { await control.KickSessionAsync(session.Id, stoppingToken); tracker.Remove(session.Id); }
                        await control.KickStreamSessionsAsync(path, stoppingToken); tracker.RemovePath(path);
                    }
                    if (item.Owner is { } owner) foreach (var session in tracker.ForOwner(owner)) { await control.KickSessionAsync(session.Id, stoppingToken); tracker.Remove(session.Id); }
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(error, "Media disconnect failed; retrying through private administration.");
                    await Task.Delay(1000, stoppingToken);
                    if (!pending.Writer.TryWrite(item)) throw new InvalidOperationException("Media disconnect retry queue is full.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await sweep; }
    }
    private async Task Sweep(PeriodicTimer timer, CancellationToken token)
    {
        try { while (await timer.WaitForNextTickAsync(token)) rooms.SweepExpired(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        rooms.StreamRevoked -= PathRevoked; rooms.SessionRevoked -= OwnerRevoked;
        await base.StopAsync(cancellationToken);
        foreach (var session in tracker.All())
        {
            try { await control.KickSessionAsync(session.Id, cancellationToken); tracker.Remove(session.Id); }
            catch (Exception error) { logger.LogError(error, "Owned media disconnection failed during host shutdown."); }
        }
    }
}
