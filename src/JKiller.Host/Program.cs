using JKiller.Host.Rooms;
using JKiller.Host.Security;
using JKiller.Host.Media;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using JKiller.Host.Diagnostics;
namespace JKiller.Host;
public sealed record HostOptions(Uri PublicOrigin, Uri MediaOrigin, Uri AdminOrigin, string BootstrapSecret, string AdminSecret, int InternalPort = 5001);
public static class HostApplication
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var publicOrigin = new Uri(builder.Configuration["PublicOrigin"] ?? "http://localhost:5000");
        var bootstrap = Environment.GetEnvironmentVariable("JKILLER_BOOTSTRAP_SECRET") ?? throw new InvalidOperationException("Start with scripts/Start-Local.ps1 to supply private bootstrap credentials.");
        var admin = Environment.GetEnvironmentVariable("JKILLER_ADMIN_SECRET") ?? throw new InvalidOperationException("Private media administration credential missing.");
        var options = new HostOptions(publicOrigin, new("http://127.0.0.1:8889"), new("http://127.0.0.1:9997"), bootstrap, admin);
        builder.WebHost.UseUrls("http://127.0.0.1:5000", "http://127.0.0.1:5001");
        var app = Build(builder, options);
        await app.RunAsync();
    }
    public static WebApplication Build(WebApplicationBuilder builder, HostOptions options)
    {
        if ((!options.PublicOrigin.IsLoopback && options.PublicOrigin.Scheme != "https") || options.PublicOrigin.AbsolutePath != "/" || !string.IsNullOrEmpty(options.PublicOrigin.UserInfo) || options.BootstrapSecret.Length < 32 || options.AdminSecret.Length < 32)
            throw new ArgumentException("Configure an HTTPS public origin (or localhost HTTP) and private bootstrap/admin secrets.");
        if (!options.MediaOrigin.IsLoopback || !options.AdminOrigin.IsLoopback) throw new ArgumentException("Media signaling and administration must stay on loopback.");
        builder.Configuration["AllowedHosts"] = options.PublicOrigin.Host + ";localhost;127.0.0.1";
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<SessionStore>(); builder.Services.AddSingleton<MediaGrantStore>(); builder.Services.AddSingleton<RoomStore>();
        builder.Services.AddSingleton<DiagnosticStore>(); builder.Services.TryAddSingleton<IHostTrafficReader, HostTrafficReader>();
        builder.Services.AddSingleton<MediaResourceStore>();
        builder.Services.AddSingleton<MediaSessionTracker>();
        builder.Services.TryAddSingleton<IMediaControl, MediaMtxClient>();
        builder.Services.AddHostedService<MediaRevocationService>();
        builder.Services.AddHostedService<RoomUpdatesService>();
        builder.Services.TryAddSingleton(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = options.MediaOrigin, Timeout = TimeSpan.FromSeconds(15) });
        builder.Services.AddSignalR(hub => { hub.MaximumReceiveMessageSize = 8192; });
        builder.Services.AddAuthentication("session").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>("session", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(rate =>
        {
            rate.RejectionStatusCode = 429;
            rate.AddPolicy("join", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ =>
                new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") || path.StartsWithSegments("/media") || path.StartsWithSegments("/media-resources") || path.StartsWithSegments("/hubs"))
            {
                var origin = context.Request.Headers.Origin;
                if ((origin.Count > 0 && (origin.Count != 1 || origin[0] != options.PublicOrigin.GetLeftPart(UriPartial.Authority))) ||
                    (origin.Count == 0 && context.Request.Cookies.ContainsKey(SessionAuthenticationHandler.CookieName) && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)))
                { context.Response.StatusCode = 403; return; }
                var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = path.StartsWithSegments("/api") ? 8192 : 262144;
                if (context.Request.ContentLength > (path.StartsWithSegments("/api") ? 8192 : 262144)) { context.Response.StatusCode = 413; return; }
            }
            try { await next(); }
            catch (UnauthorizedAccessException) when (!context.Response.HasStarted) { context.Response.StatusCode = 403; }
            catch (ArgumentException) when (!context.Response.HasStarted) { context.Response.StatusCode = 400; }
            catch (InvalidOperationException) when (!context.Response.HasStarted) { context.Response.StatusCode = 409; }
        });
        app.UseAuthentication(); app.UseAuthorization();
        app.UseRateLimiter();
        var viewerRoot = Path.GetFullPath(builder.Configuration["ViewerRoot"] ?? Path.Combine(builder.Environment.ContentRootPath, "web", "dist"));
        if (Directory.Exists(viewerRoot))
        {
            var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(viewerRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        }
        app.MapRoomEndpoints();
        app.MapDiagnostics();
        app.MapMediaProxy();
        app.MapHub<RoomHub>("/hubs/room").RequireAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        var bootstrap = app.Services.GetRequiredService<RoomStore>().CreateRoom();
        app.MapGet("/internal/bootstrap", (HttpContext context) =>
            IsPrivate(context, options) && OpaqueSecrets.Matches(context.Request.Headers.Authorization.ToString(), OpaqueSecrets.Hash("Bearer " + options.BootstrapSecret))
                ? Results.Ok(new { room = bootstrap, joinLink = options.PublicOrigin.GetLeftPart(UriPartial.Authority) + "/#invite=" + Uri.EscapeDataString(bootstrap.Invite) }) : Results.NotFound());
        app.MapMediaAuth();
        return app;
    }
    internal static bool IsPrivate(HttpContext context, HostOptions options) => context.Connection.LocalPort == options.InternalPort && context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address);
}
