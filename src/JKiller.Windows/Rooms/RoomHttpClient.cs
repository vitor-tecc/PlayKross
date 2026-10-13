using JKiller.Contracts;
using JKiller.Core.Rooms;
using System.Net.Http.Headers;
using System.Net.Http.Json;
namespace JKiller.Windows.Rooms;
public sealed class RoomHttpClient(HttpClient http) : IRoomClient
{
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
        if (grant.ExpiresAt <= DateTimeOffset.UtcNow || string.IsNullOrEmpty(grant.SessionToken)) throw new ArgumentException("The saved room session has expired.");
        Origin = origin; Session = grant;
    }
    public async Task<SessionGrant> JoinAsync(Uri roomUri, string invite, string nickname, CancellationToken token)
    {
        ValidateOrigin(roomUri);
        var grant = await Send<SessionGrant>(roomUri, HttpMethod.Post, "/api/join", new { invite, nickname, desktop = true }, Origin == roomUri, token);
        Restore(roomUri, grant); return grant;
    }
    public Task<RoomSnapshot> RenameAsync(string nickname, CancellationToken token) => Auth<RoomSnapshot>(HttpMethod.Post, "/api/me/nickname", new { nickname }, token);
    public Task<PublishReservation> ReservePublishAsync(CancellationToken token) => Auth<PublishReservation>(HttpMethod.Post, "/api/streams", null, token);
    public async Task StopPublishAsync(Guid id, CancellationToken token) => await Auth<RoomSnapshot>(HttpMethod.Delete, $"/api/streams/{id:D}", null, token);
    public Task<PlaybackGrant> GetPlaybackAsync(Guid id, CancellationToken token) => Auth<PlaybackGrant>(HttpMethod.Get, $"/api/streams/{id:D}/playback", null, token);
    private Task<T> Auth<T>(HttpMethod method, string path, object? body, CancellationToken token) => Send<T>(Origin ?? throw new InvalidOperationException("Join a room first."), method, path, body, true, token);
    private async Task<T> Send<T>(Uri origin, HttpMethod method, string path, object? body, bool authenticate, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(method, new Uri(origin, path));
        if (authenticate && Session is { } session)
        {
            if (session.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("The room session expired. Join again.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
        }
        request.Headers.Add("Origin", origin.GetLeftPart(UriPartial.Authority));
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"The room server rejected the request (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
        await response.Content.LoadIntoBufferAsync(131072, deadline.Token);
        return await response.Content.ReadFromJsonAsync<T>(deadline.Token) ?? throw new InvalidDataException("The room server returned an empty response.");
    }
}
