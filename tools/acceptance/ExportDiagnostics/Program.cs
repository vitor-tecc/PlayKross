using JKiller.Windows.Rooms;
using JKiller.Windows.Security;
using System.Text.Json;
var origin = new Uri(Environment.GetEnvironmentVariable("JKILLER_DIAGNOSTIC_ORIGIN") ?? "http://localhost:5000");
var saved = await new SessionVault(Path.Combine(Environment.CurrentDirectory, ".runtime/user-session")).RestoreAsync(origin, CancellationToken.None) ?? throw new InvalidOperationException("Join the room in the native application before exporting, or use the browser Export button.");
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
var client = new RoomHttpClient(http); client.Restore(origin, saved.Grant);
Console.WriteLine(JsonSerializer.Serialize(await client.ExportDiagnosticsAsync(CancellationToken.None), new JsonSerializerOptions { WriteIndented = true }));
