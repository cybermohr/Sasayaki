using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Sasayaki.Core;
using Xunit;

namespace Sasayaki.Tests;

public sealed class WebSocketTransportTests
{
    [Fact]
    public async Task StalledHandshakeRetriesWithAFreshSocket()
    {
        await using var server = new HandshakeServer(stallFirst: true);
        await using var transport = new WebSocketTransport();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await transport.ConnectAsync(server.Uri, "synthetic-test-key", deadline.Token);
        Assert.Equal(2, server.Attempts);
    }

    [Fact]
    public async Task CallerCancellationStopsHandshakeWithoutRetrying()
    {
        await using var server = new HandshakeServer(stallFirst: true);
        await using var transport = new WebSocketTransport();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var connecting = transport.ConnectAsync(server.Uri, "synthetic-test-key", cancellation.Token);
        await server.FirstRequest.Task.WaitAsync(deadline.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(deadline.Token));
        Assert.Equal(1, server.Attempts);
    }

    [Fact]
    public async Task RejectedCredentialsAreNotRetried()
    {
        await using var server = new HandshakeServer(reject: true);
        await using var transport = new WebSocketTransport();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<WebSocketException>(() => transport.ConnectAsync(server.Uri, "synthetic-test-key", deadline.Token));
        Assert.Equal(1, server.Attempts);
    }

    // Exercise actual ClientWebSocket cancellation and replacement against a local
    // HTTP upgrade endpoint, without Azure credentials or uploaded audio.
    private sealed class HandshakeServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task worker;
        private int attempts;
        public int Attempts => Volatile.Read(ref attempts);
        public TaskCompletionSource FirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Uri Uri { get; }

        public HandshakeServer(bool stallFirst = false, bool reject = false)
        {
            listener.Start();
            Uri = new Uri($"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            worker = ServeAsync(stallFirst, reject);
        }

        private async Task ServeAsync(bool stallFirst, bool reject)
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                string? line;
                string? key = null;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(stop.Token)))
                    if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                        key = line.Split(':', 2)[1].Trim();
                var attempt = Interlocked.Increment(ref attempts);
                FirstRequest.TrySetResult();
                if (stallFirst && attempt == 1)
                {
                    // Wait for the timed-out client to close its first connection.
                    Assert.Equal(0, await stream.ReadAsync(new byte[1], stop.Token));
                    continue;
                }
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var response = reject ? "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n\r\n"
                    : $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), stop.Token);
                await Task.Delay(Timeout.Infinite, stop.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            try { await worker; }
            catch (OperationCanceledException) { }
            finally { listener.Stop(); stop.Dispose(); }
        }
    }
}
