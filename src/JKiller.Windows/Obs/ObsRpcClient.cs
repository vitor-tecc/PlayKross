using System.Text.Json;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using JKiller.Contracts;
using JKiller.Core.Capture;
using JKiller.Windows.Capture;

namespace JKiller.Windows.Obs;

public interface IObsRpcClient
{
    Task<JsonElement> RequestAsync(string requestType, JsonElement requestData, CancellationToken cancellationToken);
}

public sealed class ObsRequestException(string request, int code, string message) : Exception($"{request} failed ({code}): {message}")
{
    public int Code { get; } = code;
}

public sealed class ObsRpcClient : IObsRpcClient, IAsyncDisposable
{
    private const int MaximumMessageBytes = 1048576;
    private readonly Stream stream;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim gate = new(1);
    private bool initialized;
    private volatile bool poisoned;

    public ObsRpcClient(Stream stream, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        this.stream = stream; this.timeout = timeout;
    }

    public static async Task<ObsRpcClient> ConnectAsync(string pipeName, ProcessIdentity helper, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        ObsRpcClient? client = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await pipe.ConnectAsync(deadline.Token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != helper.Pid ||
                !AudioPolicy.IsSameProcess(helper, ProcessIdentityReader.Read((int)serverPid)))
                throw new InvalidDataException("Private control server is not the original owned helper.");
            client = new ObsRpcClient(pipe, TimeSpan.FromSeconds(10));
            await client.InitializeAsync(deadline.Token);
            return client;
        }
        catch { if (client is not null) await client.DisposeAsync(); else await pipe.DisposeAsync(); throw; }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Serialized(async token =>
        {
            if (initialized) return true;
            var hello = await Exchange(new { op = 0 }, token);
            var data = hello.GetProperty("d");
            if (hello.GetProperty("op").GetInt32() != 0 || data.GetProperty("rpcVersion").GetInt32() != 1 ||
                data.GetProperty("transport").GetString() != "local-pipe" || !data.GetProperty("ownerVerified").GetBoolean())
                throw new InvalidDataException("Untrusted or incompatible private helper handshake.");
            var identified = await Exchange(new { op = 1, d = new { rpcVersion = 1 } }, token);
            if (identified.GetProperty("op").GetInt32() != 2 || identified.GetProperty("d").GetProperty("negotiatedRpcVersion").GetInt32() != 1)
                throw new InvalidDataException("Private helper protocol negotiation failed.");
            initialized = true;
            return true;
        }, cancellationToken);
    }

    public Task<JsonElement> RequestAsync(string requestType, JsonElement requestData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestType) || requestData.ValueKind != JsonValueKind.Object) throw new ArgumentException("Typed control request must have a name and object parameters.");
        return Serialized(async token =>
        {
            if (!initialized) throw new InvalidOperationException("Private helper has not been identified.");
            var id = Guid.NewGuid().ToString("N");
            var message = await Exchange(new { op = 6, d = new { requestId = id, requestType, requestData } }, token);
            var data = message.GetProperty("d");
            if (message.GetProperty("op").GetInt32() != 7 || data.GetProperty("requestId").GetString() != id)
                throw new InvalidDataException("Private response does not match this request.");
            var status = data.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean())
                throw new ObsRequestException(requestType, status.GetProperty("code").GetInt32(), status.TryGetProperty("comment", out var comment) ? comment.GetString() ?? "" : "");
            return data.TryGetProperty("responseData", out var response) ? response.Clone() : JsonSerializer.SerializeToElement(new { });
        }, cancellationToken);
    }

    private async Task<T> Serialized<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        await gate.WaitAsync(deadline.Token);
        try
        {
            if (poisoned) throw new InvalidOperationException("Private connection is closed; restart the owned helper.");
            try { return await action(deadline.Token); }
            catch (ObsRequestException) { throw; }
            catch { poisoned = true; await stream.DisposeAsync(); throw; }
        }
        finally { gate.Release(); }
    }

    private async Task<JsonElement> Exchange(object message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > MaximumMessageBytes) throw new InvalidDataException("Private request exceeds size limit.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
        await stream.ReadExactlyAsync(header, token);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > MaximumMessageBytes) throw new InvalidDataException("Private response exceeds size limit.");
        var payload = new byte[size]; await stream.ReadExactlyAsync(payload, token);
        using var document = JsonDocument.Parse(payload); return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync() { poisoned = true; await stream.DisposeAsync(); }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverPid);
}
