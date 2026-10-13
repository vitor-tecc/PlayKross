using System.Net;
using System.Net.Http.Json;
using JKiller.Contracts;
using JKiller.Windows.Rooms;
using JKiller.Windows.Security;
using Xunit;
namespace JKiller.Windows.Tests;
public sealed class RoomClientTests
{
    private static SessionGrant Grant => new(Guid.NewGuid(), Guid.NewGuid(), "secret-session-test", DateTimeOffset.UtcNow.AddHours(1), new(Guid.NewGuid(), 1, [], []));
    [Fact]
    public async Task SessionIsEncryptedAndBoundToServerAndExpiry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jk-vault-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vault = new SessionVault(directory); var grant = Grant;
            await vault.SaveAsync(new(new("https://room.example"), grant), CancellationToken.None);
            Assert.Equal(grant.SessionId, (await vault.RestoreAsync(new("https://room.example"), CancellationToken.None))!.Grant.SessionId);
            Assert.Null(await vault.RestoreAsync(new("https://other.example"), CancellationToken.None));
            Assert.DoesNotContain(grant.SessionToken, System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "session.dpapi"))));
            await File.WriteAllBytesAsync(Path.Combine(directory, "session.dpapi"), [1, 2, 3]);
            Assert.Null(await vault.RestoreAsync(new("https://room.example"), CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task ExpiredSavedSessionCannotBeRestored()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jk-vault-" + Guid.NewGuid().ToString("N")); var clock = new Clock();
        try
        {
            var vault = new SessionVault(directory, clock); var grant = Grant;
            await vault.SaveAsync(new(new("http://localhost:5000"), grant), CancellationToken.None); clock.Now = grant.ExpiresAt.AddSeconds(1);
            Assert.Null(await vault.RestoreAsync(new("http://localhost:5000"), CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("http://outside.example/#invite=test")]
    [InlineData("https://name:password@room.example/#invite=test")]
    [InlineData("https://room.example/path?token=test#invite=test")]
    public void UnsafeRoomLinksAreRejected(string link) => Assert.Throws<ArgumentException>(() => RoomHttpClient.ParseLink(link));
    [Fact]
    public async Task RenameAndReservationKeepSameSessionWithoutCredentialInUrl()
    {
        var grant = Grant; var handler = new Handler(grant); using var http = new HttpClient(handler); var client = new RoomHttpClient(http);
        await client.JoinAsync(new("http://localhost:5000"), "invitation", "Before", CancellationToken.None);
        await client.RenameAsync("After", CancellationToken.None); await client.ReservePublishAsync(CancellationToken.None);
        Assert.Equal(grant.SessionId, client.Session!.SessionId); Assert.Equal(2, handler.Authenticated);
    }
    [Fact]
    public async Task NativeBearerRequestsDoNotPretendToBeBrowserOriginRequests()
    {
        var handler = new Handler(Grant) { RequireNoOrigin = true }; using var http = new HttpClient(handler); var client = new RoomHttpClient(http);
        await client.JoinAsync(new("http://localhost:5000"), "invitation", "Local host broadcaster", CancellationToken.None);
        await client.ReservePublishAsync(CancellationToken.None);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Handler(SessionGrant grant) : HttpMessageHandler
    {
        public int Authenticated;
        public bool RequireNoOrigin;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Empty(request.RequestUri!.Query); Assert.Empty(request.RequestUri.Fragment);
            if (RequireNoOrigin) Assert.False(request.Headers.Contains("Origin"));
            if (request.RequestUri.AbsolutePath == "/api/join")
            {
                var body = await request.Content!.ReadAsStringAsync(token); Assert.Contains("\"desktop\":true", body);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(grant) };
            }
            Assert.Equal(grant.SessionToken, request.Headers.Authorization!.Parameter); Authenticated++;
            return new(HttpStatusCode.OK) { Content = request.RequestUri.AbsolutePath == "/api/streams" ? JsonContent.Create(new PublishReservation(Guid.NewGuid(), "generated", new("/media/generated/whip", UriKind.Relative), "media", DateTimeOffset.UtcNow.AddMinutes(1))) : JsonContent.Create(grant.Room) };
        }
    }
}
