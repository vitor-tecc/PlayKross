using JKiller.Contracts;
namespace JKiller.Core.Rooms;
public interface IRoomClient
{
    Task<SessionGrant> JoinAsync(Uri roomUri, string invite, string nickname, CancellationToken token);
    Task<RoomSnapshot> RenameAsync(string nickname, CancellationToken token);
    Task<PublishReservation> ReservePublishAsync(CancellationToken token);
    Task StopPublishAsync(Guid streamId, CancellationToken token);
    Task<PlaybackGrant> GetPlaybackAsync(Guid streamId, CancellationToken token);
}
