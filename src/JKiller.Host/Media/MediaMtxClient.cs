using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net;
namespace JKiller.Host.Media;
public interface IMediaControl
{
    Task KickStreamSessionsAsync(string path, CancellationToken token);
    Task KickSessionAsync(Guid mediaSessionId, CancellationToken token);
}
public sealed class MediaMtxClient : IMediaControl, IDisposable
{
    private readonly HttpClient client;
    public MediaMtxClient(HostOptions options)
    {
        client = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = options.AdminOrigin, Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 262144 };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("jk-admin:" + options.AdminSecret)));
    }
    public async Task KickStreamSessionsAsync(string path, CancellationToken token)
    {
        if (!Security.MediaGrantStore.IsGeneratedPath(path)) throw new ArgumentException("Invalid generated media path.");
        int page = 0, pages;
        do
        {
            using var response = await client.GetAsync($"/v3/webrtc/sessions/list?page={page}&itemsPerPage=100", token); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
            pages = json.RootElement.GetProperty("pageCount").GetInt32();
            if (pages > 50) throw new InvalidOperationException("Media session inventory exceeds configured bounds.");
            foreach (var session in json.RootElement.GetProperty("items").EnumerateArray())
                if (session.GetProperty("path").GetString() == path && Guid.TryParse(session.GetProperty("id").GetString(), out var id)) await KickSessionAsync(id, token);
            page++;
        } while (page < pages);
    }
    public async Task KickSessionAsync(Guid mediaSessionId, CancellationToken token)
    {
        using var response = await client.PostAsync($"/v3/webrtc/sessions/kick/{mediaSessionId:D}", null, token);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }
    public void Dispose() => client.Dispose();
}
