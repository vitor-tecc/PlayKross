using JKiller.Host.Security;
using System.Net;
using System.Net.Http.Headers;
namespace JKiller.Host.Media;
public static class MediaProxy
{
    private const int MaximumSdpBytes = 262144;
    private static readonly string[] responseHeaders = ["ETag", "Accept-Patch", "Link", "ID"];
    public static void MapMediaProxy(this WebApplication app)
    {
        // Exactly generated paths; no user-selected upstream host or catch-all proxy.
        app.MapMethods("/media/rooms/{room}/streams/{stream}/main/{mode}", ["POST", "OPTIONS"], Handshake);
        app.MapMethods("/media-resources/{id:guid}", ["PATCH", "DELETE"], Resource);
    }
    private static async Task Handshake(HttpContext context, string room, string stream, string mode, HostOptions options, MediaGrantStore grants, SessionStore sessions, MediaResourceStore resources, MediaSessionTracker tracker, HttpClient media, TimeProvider clock)
    {
        var path = $"rooms/{room}/streams/{stream}/main";
        if (mode is not ("whip" or "whep") || !MediaGrantStore.IsGeneratedPath(path)) { context.Response.StatusCode = 400; return; }
        var token = SessionAuthenticationHandler.Bearer(context.Request);
        if (token is null) { context.Response.StatusCode = 401; return; }
        var ownerId = grants.GetAuthorizedSession(token, path, mode == "whip" ? "publish" : "read", clock.GetUtcNow());
        var owner = ownerId is { } id ? sessions.FindActive(id) : null;
        if (owner is null || (context.Items.TryGetValue(typeof(SessionIdentity), out var cookieIdentity) && cookieIdentity is SessionIdentity cookieOwner && cookieOwner.SessionId != owner.SessionId))
        { context.Response.StatusCode = 403; return; }
        if (HttpMethods.IsPost(context.Request.Method) && !IsContentType(context.Request, "application/sdp")) { context.Response.StatusCode = 415; return; }
        var target = new Uri(options.MediaOrigin, "/" + path + "/" + mode);
        using var request = await UpstreamRequest(context, target, token);
        using var response = await media.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        MediaResource? resource = null;
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var location = response.Headers.Location;
            var uri = location is null ? null : new Uri(target, location);
            var expected = target.AbsolutePath + "/";
            if (uri is null || uri.Scheme != options.MediaOrigin.Scheme || uri.Authority != options.MediaOrigin.Authority || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !uri.AbsolutePath.StartsWith(expected, StringComparison.Ordinal) || !Guid.TryParseExact(uri.AbsolutePath[expected.Length..], "D", out _))
            { context.Response.StatusCode = 502; return; }
            Guid? mediaId = response.Headers.TryGetValues("ID", out var ids) && Guid.TryParse(ids.SingleOrDefault(), out var parsed) ? parsed : null;
            // Re-check after asynchronous SDP negotiation: revocation can race the upstream response.
            var committed = grants.CommitAuthorization(token, path, mode == "whip" ? "publish" : "read", clock.GetUtcNow(), sessionId =>
            {
                var stillActive = sessions.FindActive(sessionId);
                if (stillActive is null || stillActive.SessionId != owner.SessionId) return false;
                resource = resources.Add(stillActive, mediaId, path, uri, token);
                if (mediaId is { } trackedId) tracker.Track(trackedId, stillActive.SessionId, path, stillActive.ExpiresAt);
                return true;
            });
            if (!committed)
            {
                using var delete = new HttpRequestMessage(HttpMethod.Delete, uri);
                using var deleted = await media.SendAsync(delete, CancellationToken.None);
                context.Response.StatusCode = 403; return;
            }
            context.Response.Headers.Location = "/media-resources/" + resource!.Id.ToString("D");
        }
        try { await Forward(context, response, mode == "whip" && response.StatusCode == HttpStatusCode.Created && request.Content is not null
            ? await request.Content.ReadAsByteArrayAsync(context.RequestAborted) : null); }
        catch
        {
            if (resource is not null) { resources.Remove(resource.Id); using var delete = new HttpRequestMessage(HttpMethod.Delete, resource.Upstream); using var deleted = await media.SendAsync(delete, CancellationToken.None); }
            throw;
        }
    }
    private static async Task Resource(HttpContext context, Guid id, SessionStore sessions, MediaResourceStore resources, MediaSessionTracker tracker, HttpClient media)
    {
        var entry = resources.Find(id);
        var credential = SessionAuthenticationHandler.Bearer(context.Request);
        var owner = entry is null ? null : sessions.FindActive(entry.OwnerSessionId);
        if (credential is null && context.User.Identity?.IsAuthenticated != true) { context.Response.StatusCode = 401; return; }
        if (entry is null || owner is null ||
            !(context.Items.TryGetValue(typeof(SessionIdentity), out var authenticated) && authenticated is SessionIdentity session && session.SessionId == entry.OwnerSessionId) &&
            !(credential is not null && OpaqueSecrets.Matches(credential, entry.CredentialHash)))
        { context.Response.StatusCode = 403; return; }
        if (HttpMethods.IsPatch(context.Request.Method) && !IsContentType(context.Request, "application/trickle-ice-sdpfrag")) { context.Response.StatusCode = 415; return; }
        using var request = await UpstreamRequest(context, entry.Upstream, null);
        using var response = await media.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        if (HttpMethods.IsDelete(context.Request.Method) && (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)) { resources.Remove(id); if (entry.MediaSessionId is { } mediaId) tracker.Remove(mediaId); }
        await Forward(context, response);
    }
    private static bool IsContentType(HttpRequest request, string type) => MediaTypeHeaderValue.TryParse(request.ContentType, out var parsed) && parsed.MediaType == type;
    private static async Task<HttpRequestMessage> UpstreamRequest(HttpContext context, Uri uri, string? bearer)
    {
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), uri);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (context.Request.Headers.TryGetValue("If-Match", out var match)) request.Headers.TryAddWithoutValidation("If-Match", match.ToArray());
        if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method))
        {
            request.Content = new ByteArrayContent(await ReadBounded(context.Request.Body, context.RequestAborted));
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType!);
        }
        return request;
    }
    private static async Task<byte[]> ReadBounded(Stream input, CancellationToken token)
    {
        using var result = new MemoryStream(); var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (result.Length + count > MaximumSdpBytes) throw new BadHttpRequestException("SDP size limit exceeded.", 413);
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
    private static async Task Forward(HttpContext context, HttpResponseMessage response, byte[]? whipOffer = null)
    {
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotModified || HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in responseHeaders) if (response.Headers.TryGetValues(header, out var values)) context.Response.Headers[header] = values.ToArray();
            return;
        }
        var body = await ReadBounded(await response.Content.ReadAsStreamAsync(context.RequestAborted), context.RequestAborted);
        if (whipOffer is not null && response.Content.Headers.ContentType?.MediaType == "application/sdp") body = WhipH264Capabilities.Advertise1080p60(whipOffer, body);
        context.Response.StatusCode = (int)response.StatusCode;
        if (response.Content.Headers.ContentType is { } type) context.Response.ContentType = type.ToString();
        foreach (var header in responseHeaders) if (response.Headers.TryGetValues(header, out var values)) context.Response.Headers[header] = values.ToArray();
        await context.Response.Body.WriteAsync(body, context.RequestAborted);
    }
}
