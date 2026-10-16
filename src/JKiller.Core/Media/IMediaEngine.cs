using JKiller.Contracts;
namespace JKiller.Core.Media;

public interface IMediaEngine
{
    event EventHandler<Exception>? SafetyFault;
    Task<AppliedMediaSettings> PrepareAsync(BroadcastRequest request, CancellationToken cancellationToken);
    Task StartAsync(Uri whipUri, string token, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
