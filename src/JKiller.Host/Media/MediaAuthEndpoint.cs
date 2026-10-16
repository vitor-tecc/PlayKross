using JKiller.Host.Security;
using System.Net;
namespace JKiller.Host.Media;
public sealed record MediaAuthRequest(string? User, string? Password, string? Token, string? Ip, string? Action, string? Path, string? Protocol, string? Id, string? Query, string? UserAgent);
public static class MediaAuthEndpoint
{
    public static bool Authorize(MediaAuthRequest request, HostOptions options, MediaGrantStore grants, SessionStore sessions, MediaSessionTracker tracker, TimeProvider clock)
    {
        if (request.Action is "api" or "metrics")
            return request.User == "jk-admin" && request.Password is { Length: <= 128 } password && IPAddress.TryParse(request.Ip, out var ip) && IPAddress.IsLoopback(ip) && OpaqueSecrets.Matches(password, OpaqueSecrets.Hash(options.AdminSecret));
        if (request.Protocol != "webrtc" || request.Action is not ("publish" or "read") || request.Path is null || request.Token is null) return false;
        return grants.CommitAuthorization(request.Token, request.Path, request.Action, clock.GetUtcNow(), id =>
        {
            var owner = sessions.FindActive(id);
            if (owner is null) return false;
            if (string.IsNullOrEmpty(request.Id)) return true; // OPTIONS has no established session yet.
            return Guid.TryParseExact(request.Id, "D", out var mediaId) && mediaId != Guid.Empty && tracker.Track(mediaId, owner.SessionId, request.Path, owner.ExpiresAt);
        });
    }
    public static void MapMediaAuth(this WebApplication app)
    {
        app.MapPost("/internal/media-auth", async (HttpContext context, HostOptions options, MediaGrantStore grants, SessionStore sessions, MediaSessionTracker tracker, TimeProvider clock) =>
        {
            if (!HostApplication.IsPrivate(context, options)) return Results.NotFound();
            if (context.Request.ContentLength > 8192) return Results.StatusCode(413);
            var request = await context.Request.ReadFromJsonAsync<MediaAuthRequest>(context.RequestAborted);
            return request is not null && Authorize(request, options, grants, sessions, tracker, clock) ? Results.Ok() : Results.Unauthorized();
        });
    }
}
