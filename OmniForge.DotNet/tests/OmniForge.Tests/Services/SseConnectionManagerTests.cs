using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Web.Services;
using System.IO;
using System.Text;
using System.Text.Json;
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
}
