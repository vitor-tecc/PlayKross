using System.Buffers.Binary;
using System.Text.Json;
using JKiller.Windows.Obs;
using Xunit;
using System.IO.Pipes;
using JKiller.Windows.Capture;

namespace JKiller.Windows.Tests;

public sealed class ObsRpcTests
{
    [Fact]
    public async Task RealPipeServerMustMatchOriginalHelperCreationIdentity()
    {
        var name = "jk-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = server.WaitForConnectionAsync();
        var identity = ProcessIdentityReader.Read(Environment.ProcessId)!;
        await Assert.ThrowsAsync<InvalidDataException>(() => ObsRpcClient.ConnectAsync(name, identity with { CreationFileTime = identity.CreationFileTime + 1 }, CancellationToken.None));
        await connected.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task OwnerAuthenticatedHelloIdentifyAndFragmentedResponse()
    {
        await using var stream = new ReplyStream();
        await using var rpc = new ObsRpcClient(stream, TimeSpan.FromSeconds(1));
        await rpc.InitializeAsync();
        var result = await rpc.RequestAsync("GetVersion", JsonSerializer.SerializeToElement(new { }));
        Assert.Equal("32.2.2", result.GetProperty("obsVersion").GetString());
        Assert.Equal(new[] { 0, 1, 6 }, stream.Operations);
    }

    [Theory]
    [InlineData(false, "local-pipe", 1)]
    [InlineData(true, "network", 1)]
    [InlineData(true, "local-pipe", 2)]
    public async Task RejectsUntrustedOrIncompatibleHello(bool owner, string transport, int version)
    {
        await using var stream = new ReplyStream { Owner = owner, Transport = transport, Version = version };
        await using var rpc = new ObsRpcClient(stream, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<InvalidDataException>(() => rpc.InitializeAsync());
    }

    [Fact]
    public async Task WrongRequestIdPoisonsConnection()
    {
        await using var stream = new ReplyStream { WrongId = true };
        await using var rpc = new ObsRpcClient(stream, TimeSpan.FromSeconds(1));
        await rpc.InitializeAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => rpc.RequestAsync("GetStats", JsonSerializer.SerializeToElement(new { })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rpc.RequestAsync("GetStats", JsonSerializer.SerializeToElement(new { })));
    }

    [Fact]
    public async Task TimeoutAndCancellationAreBounded()
    {
        await using var stream = new ReplyStream();
        await using var rpc = new ObsRpcClient(stream, TimeSpan.FromMilliseconds(80));
        await rpc.InitializeAsync();
        stream.Stall = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rpc.RequestAsync("GetStats", JsonSerializer.SerializeToElement(new { })));
        await using var second = new ObsRpcClient(new ReplyStream { Stall = true }, TimeSpan.FromSeconds(10));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.InitializeAsync(canceled.Token));
    }

    [Fact]
    public async Task OversizedReplyRejectedBeforeAllocation()
    {
        await using var stream = new ReplyStream { Oversized = true };
        await using var rpc = new ObsRpcClient(stream, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<InvalidDataException>(() => rpc.InitializeAsync());
    }

    // Fragmented byte transport exercises the real framing/client, without OBS or media.
    private sealed class ReplyStream : Stream
    {
        private readonly MemoryStream written = new();
        private byte[] reply = []; private int offset;
        public List<int> Operations { get; } = [];
        public bool Owner { get; init; } = true;
        public string Transport { get; init; } = "local-pipe";
        public int Version { get; init; } = 1;
        public bool WrongId { get; init; }
        public bool Oversized { get; init; }
        public bool Stall { get; set; }
        public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            var bytes = written.ToArray(); written.SetLength(0);
            using var request = JsonDocument.Parse(bytes.AsMemory(4));
            var op = request.RootElement.GetProperty("op").GetInt32(); Operations.Add(op);
            object response = op switch
            {
                0 => new { op = 0, d = new { rpcVersion = Version, transport = Transport, ownerVerified = Owner } },
                1 => new { op = 2, d = new { negotiatedRpcVersion = 1 } },
                _ => new { op = 7, d = new { requestId = WrongId ? "wrong" : request.RootElement.GetProperty("d").GetProperty("requestId").GetString(), requestStatus = new { result = true, code = 100 }, responseData = new { obsVersion = "32.2.2" } } }
            };
            var json = JsonSerializer.SerializeToUtf8Bytes(response);
            reply = new byte[4 + json.Length]; BinaryPrimitives.WriteInt32LittleEndian(reply, Oversized ? 1048577 : json.Length); json.CopyTo(reply, 4); offset = 0;
            return Task.CompletedTask;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Stall) await Task.Delay(Timeout.Infinite, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var size = Math.Min(Math.Min(3, buffer.Length), reply.Length - offset);
            reply.AsMemory(offset, size).CopyTo(buffer); offset += size; return size;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => written.WriteAsync(buffer, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
