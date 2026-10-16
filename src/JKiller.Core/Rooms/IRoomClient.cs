using JKiller.Contracts;
namespace JKiller.Core.Rooms;
public interface IRoomClient
{
    Task<SessionGrant> JoinAsync(Uri roomUri, string invite, string nickname, CancellationToken token);
    Task<RoomSnapshot> RenameAsync(string nickname, CancellationToken token);
    Task HeartbeatAsync(CancellationToken token) => Task.CompletedTask;
    Task LeaveAsync(CancellationToken token) => Task.CompletedTask;
    Task<PublishReservation> ReservePublishAsync(CancellationToken token);
    Task RenewPublishAsync(Guid streamId, CancellationToken token);
    Task StopPublishAsync(Guid streamId, CancellationToken token);
    Task<PlaybackGrant> GetPlaybackAsync(Guid streamId, CancellationToken token);
}
