using JKiller.Contracts;
using JKiller.Core.Rooms;
using System.Net.Http.Headers;
using System.Net.Http.Json;
namespace JKiller.Windows.Rooms;
public sealed class RoomHttpClient(HttpClient http, TimeProvider? timeProvider = null) : IRoomClient
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public SessionGrant? Session { get; private set; }
    public Uri? Origin { get; private set; }
    public static (Uri Origin, string Invite) ParseLink(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) throw new ArgumentException("Enter a complete room link.");
        ValidateOrigin(uri);
        if (!uri.Fragment.StartsWith("#invite=", StringComparison.Ordinal) || uri.Fragment.Length > 1024) throw new ArgumentException("The room link must include its invitation.");
        return (new(uri.GetLeftPart(UriPartial.Authority)), Uri.UnescapeDataString(uri.Fragment[8..]));
    }
    private static void ValidateOrigin(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0)
            throw new ArgumentException("Use the server's HTTPS room link, or localhost HTTP for local testing.");
    }
    public void Restore(Uri origin, SessionGrant grant)
    {
        ValidateOrigin(origin);
        if (grant.ExpiresAt <= clock.GetUtcNow() || string.IsNullOrEmpty(grant.SessionToken)) throw new ArgumentException("The saved room session has expired.");
        Origin = origin; Session = grant;
    }
    public async Task<SessionGrant> JoinAsync(Uri roomUri, string invite, string nickname, CancellationToken token)
    {
        ValidateOrigin(roomUri);
        if (Session?.ExpiresAt <= clock.GetUtcNow()) Session = null;
        var grant = await Send<SessionGrant>(roomUri, HttpMethod.Post, "/api/join", new { invite, nickname, desktop = true }, Origin == roomUri, token);
        Restore(roomUri, grant); return grant;
    }
    public Task<RoomSnapshot> RenameAsync(string nickname, CancellationToken token) => Auth<RoomSnapshot>(HttpMethod.Post, "/api/me/nickname", new { nickname }, token);
    public async Task HeartbeatAsync(CancellationToken token) => await Auth<RoomSnapshot>(HttpMethod.Post, "/api/me/presence", null, token);
    public async Task LeaveAsync(CancellationToken token)
    {
        try
        {
            if (Session is not null && Session.ExpiresAt > clock.GetUtcNow())
                await Auth<RoomSnapshot>(HttpMethod.Post, "/api/me/leave", null, token);
        }
        catch (InvalidOperationException) when (Session?.ExpiresAt <= clock.GetUtcNow()) { /* Expiry can race the send check. */ }
        finally { Session = null; Origin = null; }
    }
    public async Task<PublishReservation> ReservePublishAsync(CancellationToken token)
    {
        try { return await Auth<PublishReservation>(HttpMethod.Post, "/api/streams", null, token); }
        catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("This version allows one broadcaster per room. Stop the current stream before starting another. Watching your friend's stream is still supported.", error);
        }
    }
    public async Task RenewPublishAsync(Guid id, CancellationToken token) => await Auth<RoomSnapshot>(HttpMethod.Post, $"/api/streams/{id:D}/lease", null, token);
    public async Task StopPublishAsync(Guid id, CancellationToken token) => await Auth<RoomSnapshot>(HttpMethod.Delete, $"/api/streams/{id:D}", null, token);
    public Task<PlaybackGrant> GetPlaybackAsync(Guid id, CancellationToken token) => Auth<PlaybackGrant>(HttpMethod.Get, $"/api/streams/{id:D}/playback", null, token);
    public Task<System.Text.Json.JsonElement> ReportDiagnosticsAsync(DiagnosticsEnvelope sample, CancellationToken token) => Auth<System.Text.Json.JsonElement>(HttpMethod.Post, "/api/diagnostics", sample, token);
    public Task<RedactedReport> ExportDiagnosticsAsync(CancellationToken token) => Auth<RedactedReport>(HttpMethod.Get, "/api/diagnostics", null, token);
    private Task<T> Auth<T>(HttpMethod method, string path, object? body, CancellationToken token) => Send<T>(Origin ?? throw new InvalidOperationException("Join a room first."), method, path, body, true, token);
    private async Task<T> Send<T>(Uri origin, HttpMethod method, string path, object? body, bool authenticate, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(method, new Uri(origin, path));
        if (authenticate && Session is { } session)
        {
            if (session.ExpiresAt <= clock.GetUtcNow()) throw new InvalidOperationException("The room session expired. Join again.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
        }
        // This client uses bearer headers and no cookie jar, not browser ambient credentials.
        // Omitting Origin also permits the host PC to use localhost behind its public proxy.
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"The room server rejected the request (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
        await response.Content.LoadIntoBufferAsync(path == "/api/diagnostics" && method == HttpMethod.Get ? 8388608 : 131072, deadline.Token);
        return await response.Content.ReadFromJsonAsync<T>(deadline.Token) ?? throw new InvalidDataException("The room server returned an empty response.");
    }
}
