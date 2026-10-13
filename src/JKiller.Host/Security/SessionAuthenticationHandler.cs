using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
namespace JKiller.Host.Security;
public sealed class SessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SessionStore sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string CookieName = "jk-session";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Bearer(Request);
        // Media grants are separate bearer capabilities, never session identities.
        if (token is null || !token.StartsWith("s.", StringComparison.Ordinal)) token = Request.Cookies[CookieName];
        var session = token is null ? null : sessions.Authenticate(token);
        if (session is null) return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, session.SessionId.ToString()), new Claim("room", session.RoomId.ToString())], Scheme.Name));
        Context.Items[typeof(SessionIdentity)] = session;
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
    public static string? Bearer(HttpRequest request)
    {
        var header = request.Headers.Authorization;
        return header.Count == 1 && header[0] is { Length: <= 256 } value && value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : null;
    }
    public static SessionIdentity Require(HttpContext context) => context.Items.TryGetValue(typeof(SessionIdentity), out var identity) && identity is SessionIdentity session ? session : throw new UnauthorizedAccessException();
}
