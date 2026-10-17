using JKiller.Contracts;
using JKiller.Host.Rooms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using JKiller.Host.Media;
using JKiller.Host.Diagnostics;
namespace JKiller.Host.Tests;
internal sealed class HostFixture : IAsyncDisposable
{
    public WebApplication App { get; }
    public HttpClient Client { get; private set; } = null!;
    public TestClock Clock { get; } = new();
    public FakeMediaControl Control { get; } = new();
    public HostOptions Options { get; } = new(new("https://room.test"), new("http://127.0.0.1:8889"), new("http://127.0.0.1:9997"), new string('b', 43), new string('a', 43));
    private HostFixture(HttpMessageHandler? media = null, IHostTrafficReader? traffic = null)
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddSingleton<IMediaControl>(Control);
        if (traffic is not null) builder.Services.AddSingleton(traffic);
        if (media is not null) builder.Services.AddSingleton(new HttpClient(media) { BaseAddress = Options.MediaOrigin });
        App = HostApplication.Build(builder, Options);
    }
    public static async Task<HostFixture> Start(HttpMessageHandler? media = null, IHostTrafficReader? traffic = null)
    {
        var fixture = new HostFixture(media, traffic); await fixture.App.StartAsync(); fixture.Client = fixture.App.GetTestClient(); fixture.Client.BaseAddress = fixture.Options.PublicOrigin; return fixture;
    }
    public string Invite => App.Services.GetRequiredService<RoomStore>().CreateRoom().Invite;
    public async Task<SessionGrant> Join(string? invite = null, string nickname = "friend")
    {
        var response = await Client.PostAsJsonAsync("/api/join", new { invite = invite ?? Invite, nickname, desktop = true });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<SessionGrant>())!;
    }
    public HttpRequestMessage Request(HttpMethod method, string url, string credential, object? json = null)
    {
        var request = new HttpRequestMessage(method, url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        if (json is not null) request.Content = JsonContent.Create(json); return request;
    }
    public async ValueTask DisposeAsync() { Client.Dispose(); await App.StopAsync(); await App.DisposeAsync(); }
}
internal sealed class FakeMediaControl : IMediaControl
{
    public System.Collections.Concurrent.ConcurrentBag<string> Paths = [];
    public System.Collections.Concurrent.ConcurrentBag<Guid> Ids = [];
    public Task KickStreamSessionsAsync(string path, CancellationToken token) { Paths.Add(path); return Task.CompletedTask; }
    public Task KickSessionAsync(Guid id, CancellationToken token) { Ids.Add(id); return Task.CompletedTask; }
}
