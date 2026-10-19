using JKiller.Contracts;
using JKiller.Host.Security;
using Microsoft.AspNetCore.SignalR;
namespace JKiller.Host.Rooms;
public sealed record JoinRequest(string Invite, string Nickname, bool Desktop = false);
public sealed record RenameRequest(string Nickname);
public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this WebApplication app)
    {
        app.MapPost("/api/join", async (JoinRequest request, HttpContext context, RoomStore rooms, HostOptions options, IHubContext<RoomHub> hub, RoomConnections connections) =>
        {
            SessionGrant session;
            if (context.Items.TryGetValue(typeof(SessionIdentity), out var authenticated) && authenticated is SessionIdentity previous && rooms.MatchesInvite(previous.RoomId, request.Invite))
            {
                var credential = SessionAuthenticationHandler.Bearer(context.Request);
                if (credential is null || !credential.StartsWith("s.", StringComparison.Ordinal)) credential = context.Request.Cookies[SessionAuthenticationHandler.CookieName];
                session = new(previous.RoomId, previous.SessionId, credential!, previous.ExpiresAt, rooms.Rejoin(previous.SessionId, request.Nickname));
            }
            else session = rooms.Join(request.Invite, request.Nickname);
            context.Response.Cookies.Append(SessionAuthenticationHandler.CookieName, session.SessionToken, new CookieOptions { HttpOnly = true, Secure = options.PublicOrigin.Scheme == "https", SameSite = SameSiteMode.Strict, Path = "/", Expires = session.ExpiresAt, IsEssential = true });
            await connections.SendAsync(hub, session.Room);
            return Results.Ok(request.Desktop ? session : session with { SessionToken = "" });
        }).RequireRateLimiting("join");
        app.MapGet("/api/me", (HttpContext context, RoomStore rooms) =>
        {
            var session = SessionAuthenticationHandler.Require(context);
            return Results.Ok(new { sessionId = session.SessionId, expiresAt = session.ExpiresAt, room = rooms.Snapshot(session.SessionId) });
        }).RequireAuthorization();
        app.MapGet("/api/room", (HttpContext context, RoomStore rooms) => Results.Ok(rooms.Snapshot(SessionAuthenticationHandler.Require(context).SessionId))).RequireAuthorization();
        app.MapPost("/api/me/presence", (HttpContext context, RoomStore rooms) =>
            Results.Ok(rooms.Heartbeat(SessionAuthenticationHandler.Require(context).SessionId))).RequireAuthorization();
        app.MapPost("/api/me/leave", (HttpContext context, RoomStore rooms) =>
        {
            var snapshot = rooms.Leave(SessionAuthenticationHandler.Require(context).SessionId);
            context.Response.Cookies.Delete(SessionAuthenticationHandler.CookieName, new CookieOptions { Path = "/" });
            return Results.Ok(snapshot);
        }).RequireAuthorization();
        app.MapPost("/api/me/nickname", async (RenameRequest request, HttpContext context, RoomStore rooms, IHubContext<RoomHub> hub, RoomConnections connections) =>
        {
            var snapshot = rooms.Rename(SessionAuthenticationHandler.Require(context).SessionId, request.Nickname); await connections.SendAsync(hub, snapshot); return Results.Ok(snapshot);
        }).RequireAuthorization();
        app.MapPost("/api/streams", async (bool? requireNew, HttpContext context, RoomStore rooms, IHubContext<RoomHub> hub, RoomConnections connections) =>
        {
            var session = SessionAuthenticationHandler.Require(context); var stream = rooms.ReservePublish(session.SessionId, requireNew == true);
            await connections.SendAsync(hub, rooms.Snapshot(session.SessionId)); return Results.Ok(stream);
        }).RequireAuthorization();
        app.MapDelete("/api/streams/{id:guid}", async (Guid id, HttpContext context, RoomStore rooms, IHubContext<RoomHub> hub, RoomConnections connections) =>
        {
            var snapshot = rooms.StopPublish(SessionAuthenticationHandler.Require(context).SessionId, id); await connections.SendAsync(hub, snapshot); return Results.Ok(snapshot);
        }).RequireAuthorization();
        app.MapPost("/api/streams/{id:guid}/lease", (Guid id, HttpContext context, RoomStore rooms) =>
            Results.Ok(rooms.RenewPublish(SessionAuthenticationHandler.Require(context).SessionId, id))).RequireAuthorization();
        app.MapGet("/api/streams/{id:guid}/playback", (Guid id, HttpContext context, RoomStore rooms) => Results.Ok(rooms.GetPlayback(SessionAuthenticationHandler.Require(context).SessionId, id))).RequireAuthorization();
    }

}
