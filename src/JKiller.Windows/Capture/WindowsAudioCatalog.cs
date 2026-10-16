using JKiller.Contracts;
namespace JKiller.Windows.Capture;

public enum AudioEndpointFlow { Render, Capture }
public enum AudioEndpointState { Active = 1, Disabled = 2, NotPresent = 4, Unplugged = 8 }
public sealed record AudioEndpointObservation(string Id, string? FriendlyName, AudioEndpointFlow Flow, AudioEndpointState State);
public sealed record AudioEndpointSnapshot(IReadOnlyList<AudioEndpointObservation> Endpoints, string? DefaultRenderEndpointId);

public interface IWindowsAudioCatalog
{
    Task<IReadOnlyList<AudioOutputDescriptor>> ListOutputsAsync(CancellationToken token);
    Task<AudioOutputDescriptor?> ResolveDefaultAsync(CancellationToken token);
}

public sealed class WindowsAudioCatalog : IWindowsAudioCatalog
{
    private readonly Func<CancellationToken, AudioEndpointSnapshot> _observe;
    public WindowsAudioCatalog() : this(CoreAudioInterop.ReadActiveRenderEndpoints) { }
    public WindowsAudioCatalog(Func<CancellationToken, AudioEndpointSnapshot> observe) => _observe = observe;

    public Task<IReadOnlyList<AudioOutputDescriptor>> ListOutputsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = _observe(token);
        token.ThrowIfCancellationRequested();
        List<AudioOutputDescriptor> outputs = [];
        foreach (var endpoint in snapshot.Endpoints)
        {
            token.ThrowIfCancellationRequested();
            if (endpoint.Flow != AudioEndpointFlow.Render || endpoint.State != AudioEndpointState.Active) continue;
            outputs.Add(new(endpoint.Id, string.IsNullOrWhiteSpace(endpoint.FriendlyName) ? endpoint.Id : endpoint.FriendlyName,
                string.Equals(endpoint.Id, snapshot.DefaultRenderEndpointId, StringComparison.Ordinal)));
        }
        return Task.FromResult<IReadOnlyList<AudioOutputDescriptor>>(outputs);
    }

    public async Task<AudioOutputDescriptor?> ResolveDefaultAsync(CancellationToken token) =>
        (await ListOutputsAsync(token)).FirstOrDefault(output => output.IsDefault);
}
