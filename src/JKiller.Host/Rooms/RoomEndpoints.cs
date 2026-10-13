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
        app.MapPost("/api/join", async (JoinRequest request, HttpContext context, RoomStore rooms, HostOptions options, IHubContext<RoomHub> hub) =>
        {
            var session = rooms.Join(request.Invite, request.Nickname);
            context.Response.Cookies.Append(SessionAuthenticationHandler.CookieName, session.SessionToken, new CookieOptions { HttpOnly = true, Secure = options.PublicOrigin.Scheme == "https", SameSite = SameSiteMode.Strict, Path = "/", Expires = session.ExpiresAt, IsEssential = true });
            await hub.Clients.Group(session.RoomId.ToString("N")).SendAsync("room", session.Room);
            return Results.Ok(request.Desktop ? session : session with { SessionToken = "" });
        }).RequireRateLimiting("join");
        app.MapGet("/api/room", (HttpContext context, RoomStore rooms) => Results.Ok(rooms.Snapshot(SessionAuthenticationHandler.Require(context).SessionId))).RequireAuthorization();
        app.MapPost("/api/me/nickname", async (RenameRequest request, HttpContext context, RoomStore rooms, IHubContext<RoomHub> hub) =>
        {
            var snapshot = rooms.Rename(SessionAuthenticationHandler.Require(context).SessionId, request.Nickname); await Send(hub, snapshot); return Results.Ok(snapshot);
        }).RequireAuthorization();
        app.MapPost("/api/streams", async (HttpContext context, RoomStore rooms, IHubContext<RoomHub> hub) =>
        {
            var session = SessionAuthenticationHandler.Require(context); var stream = rooms.ReservePublish(session.SessionId);
            await Send(hub, rooms.Snapshot(session.SessionId)); return Results.Ok(stream);
        }).RequireAuthorization();
        app.MapDelete("/api/streams/{id:guid}", async (Guid id, HttpContext context, RoomStore rooms, IHubContext<RoomHub> hub) =>
        {
            var snapshot = rooms.StopPublish(SessionAuthenticationHandler.Require(context).SessionId, id); await Send(hub, snapshot); return Results.Ok(snapshot);
        }).RequireAuthorization();
        app.MapGet("/api/streams/{id:guid}/playback", (Guid id, HttpContext context, RoomStore rooms) => Results.Ok(rooms.GetPlayback(SessionAuthenticationHandler.Require(context).SessionId, id))).RequireAuthorization();
    }
    private static Task Send(IHubContext<RoomHub> hub, RoomSnapshot snapshot) => hub.Clients.Group(snapshot.RoomId.ToString("N")).SendAsync("room", snapshot);
}
