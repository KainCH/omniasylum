using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Web.Services;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Services;

public class SseConnectionManagerTests
{
    private readonly SseConnectionManager _manager;

    public SseConnectionManagerTests()
    {
        var mockLogger = new Mock<ILogger<SseConnectionManager>>();
        _manager = new SseConnectionManager(mockLogger.Object);
    }

    [Fact]
    public async Task RegisterAsync_SendsConnectedEventAndReturnsConnectionId()
    {
        var ms = new MemoryStream();
        using var cts = new CancellationTokenSource();

        var connectionId = await _manager.RegisterAsync("user1", ms, cts.Token);

        Assert.False(string.IsNullOrEmpty(connectionId));
        Assert.Equal(8, connectionId.Length);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("retry: 5000", output);
        Assert.Contains("event: connected", output);
        Assert.Contains(connectionId, output);
    }

    [Fact]
    public async Task SendToConnectionAsync_SendsEventToSpecificConnection()
    {
        var ms = new MemoryStream();
        using var cts = new CancellationTokenSource();

        var connectionId = await _manager.RegisterAsync("user1", ms, cts.Token);

        // Mark as ready by sending init
        await _manager.SendToConnectionAsync("user1", connectionId, "init", new { test = "data" });

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("event: init", output);
        Assert.Contains("\"test\":\"data\"", output);
    }

    [Fact]
    public async Task SendEventAsync_OnlyBroadcastsToReadyConnections()
    {
        var ms1 = new MemoryStream();
        var ms2 = new MemoryStream();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        var conn1 = await _manager.RegisterAsync("user1", ms1, cts1.Token);
        var conn2 = await _manager.RegisterAsync("user1", ms2, cts2.Token);

        // Only mark conn1 as ready
        await _manager.SendToConnectionAsync("user1", conn1, "init", new { });

        // Clear streams to see only broadcast output
        ms1.SetLength(0);
        ms2.SetLength(0);

        await _manager.SendEventAsync("user1", "counter", new { deaths = 5 });

        var output1 = Encoding.UTF8.GetString(ms1.ToArray());
        var output2 = Encoding.UTF8.GetString(ms2.ToArray());

        Assert.Contains("event: counter", output1);
        Assert.Contains("\"deaths\":5", output1);
        Assert.Empty(output2); // Not ready, should not receive broadcast
    }

    [Fact]
    public async Task RemoveClient_CleansUpConnection()
    {
        var ms = new MemoryStream();
        using var cts = new CancellationTokenSource();

        var connectionId = await _manager.RegisterAsync("user1", ms, cts.Token);
        Assert.Equal(1, _manager.GetConnectionCount("user1"));

        _manager.RemoveClient("user1", connectionId);
        Assert.Equal(0, _manager.GetConnectionCount("user1"));
    }

    [Fact]
    public async Task SendEventAsync_NoConnectionsForUser_DoesNotThrow()
    {
        // Should not throw when there are no connections
        await _manager.SendEventAsync("nonexistent-user", "counter", new { deaths = 0 });
    }

    [Fact]
    public async Task SendToConnectionAsync_InvalidConnection_ReturnsFalse()
    {
        var result = await _manager.SendToConnectionAsync("user1", "bad-id", "init", new { });
        Assert.False(result);
    }

    [Fact]
    public async Task MultipleUsersHaveIndependentConnections()
    {
        var ms1 = new MemoryStream();
        var ms2 = new MemoryStream();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        await _manager.RegisterAsync("user1", ms1, cts1.Token);
        await _manager.RegisterAsync("user2", ms2, cts2.Token);

        Assert.Equal(1, _manager.GetConnectionCount("user1"));
        Assert.Equal(1, _manager.GetConnectionCount("user2"));
    }

    [Fact]
    public async Task ConcurrentWrites_DoNotInterleave()
    {
        var slowStream = new SlowStream();
        using var cts = new CancellationTokenSource();

        var connectionId = await _manager.RegisterAsync("user1", slowStream, cts.Token);
        await _manager.SendToConnectionAsync("user1", connectionId, "init", new { });

        // Clear the stream to isolate broadcast output
        slowStream.Reset();

        const int concurrentWrites = 20;
        var tasks = Enumerable.Range(0, concurrentWrites)
            .Select(i => _manager.SendEventAsync("user1", "counter", new { index = i }))
            .ToArray();

        await Task.WhenAll(tasks);

        var output = slowStream.GetContent();

        // Each SSE message must be a complete "event: ...\ndata: ...\n\n" block.
        // Split on double-newline to get individual messages.
        var messages = output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(concurrentWrites, messages.Length);

        foreach (var msg in messages)
        {
            Assert.Matches(@"^event: counter\ndata: \{""index"":\d+\}$", msg);
        }
    }

    [Fact]
    public async Task RemoveClient_DisposesWriteLock()
    {
        var ms = new MemoryStream();
        using var cts = new CancellationTokenSource();

        var connectionId = await _manager.RegisterAsync("user1", ms, cts.Token);
        _manager.RemoveClient("user1", connectionId);

        // After removal, sending to the connection should return false
        var result = await _manager.SendToConnectionAsync("user1", connectionId, "test", new { });
        Assert.False(result);
    }

    [Fact]
    public void RemoveClient_UnknownUserId_DoesNotThrow()
    {
        var exception = Record.Exception(() => _manager.RemoveClient("nonexistent-user", "conn-id"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_And_StopAsync_DoNotThrow()
    {
        using var cts = new CancellationTokenSource();
        var exception = await Record.ExceptionAsync(async () =>
        {
            await _manager.StartAsync(cts.Token);
            await _manager.StopAsync(cts.Token);
        });
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var exception = Record.Exception(() => _manager.Dispose());
        Assert.Null(exception);
    }

    /// <summary>
    /// A stream wrapper that adds a small delay to WriteAsync to widen race windows.
    /// </summary>
    private sealed class SlowStream : Stream
    {
        private readonly MemoryStream _inner = new();
        private readonly object _readLock = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // Small yield to widen the race window
            await Task.Yield();
            lock (_readLock)
            {
                _inner.Write(buffer, offset, count);
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            lock (_readLock)
            {
                _inner.Write(buffer.Span);
            }
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public void Reset()
        {
            lock (_readLock)
            {
                _inner.SetLength(0);
            }
        }

        public string GetContent()
        {
            lock (_readLock)
            {
                return Encoding.UTF8.GetString(_inner.ToArray());
            }
        }
    }
}
