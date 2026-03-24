using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Services;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Services;

public class SseOverlayNotifierTests
{
    private readonly SseConnectionManager _sseManager;
    private readonly SseOverlayNotifier _notifier;
    private readonly Mock<IAlertRepository> _mockAlertRepository;
    private readonly MemoryStream _clientStream;
    private readonly string _connectionId;

    public SseOverlayNotifierTests()
    {
        var managerLogger = new Mock<ILogger<SseConnectionManager>>();
        _sseManager = new SseConnectionManager(managerLogger.Object);

        _mockAlertRepository = new Mock<IAlertRepository>();
        _mockAlertRepository.Setup(x => x.GetAlertsAsync(It.IsAny<string>())).ReturnsAsync(new List<Alert>());

        var enricherLogger = new Mock<ILogger<AlertPayloadEnricher>>();
        var enricher = new AlertPayloadEnricher(_mockAlertRepository.Object, enricherLogger.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(x => x.ServiceProvider).Returns(mockServiceProvider.Object);
        mockServiceProvider.Setup(x => x.GetService(typeof(IAlertPayloadEnricher))).Returns(enricher);

        var notifierLogger = new Mock<ILogger<SseOverlayNotifier>>();
        _notifier = new SseOverlayNotifier(_sseManager, mockScopeFactory.Object, notifierLogger.Object);

        // Register and ready a test client
        _clientStream = new MemoryStream();
        using var cts = new CancellationTokenSource();
        _connectionId = _sseManager.RegisterAsync("user1", _clientStream, cts.Token).GetAwaiter().GetResult();
        _sseManager.SendToConnectionAsync("user1", _connectionId, "init", new { }).GetAwaiter().GetResult();

        // Clear stream to isolate subsequent writes
        _clientStream.SetLength(0);
    }

    [Fact]
    public async Task NotifyCounterUpdateAsync_SendsCounterEvent()
    {
        var counter = new Counter { TwitchUserId = "user1", Deaths = 5, Swears = 3, Screams = 1, Bits = 100 };

        await _notifier.NotifyCounterUpdateAsync("user1", counter);

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: counter", output);
        Assert.Contains("\"deaths\":5", output);
        Assert.Contains("\"swears\":3", output);
    }

    [Fact]
    public async Task NotifySettingsUpdateAsync_SendsConfigEvent()
    {
        var settings = new OverlaySettings { Position = "bottom-left", Scale = 1.5 };

        await _notifier.NotifySettingsUpdateAsync("user1", settings);

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: config", output);
        Assert.Contains("bottom-left", output);
    }

    [Fact]
    public async Task NotifyStreamStartedAsync_SendsStreamLiveEvent()
    {
        var counter = new Counter { TwitchUserId = "user1", StreamStarted = System.DateTimeOffset.UtcNow };

        await _notifier.NotifyStreamStartedAsync("user1", counter);

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: stream", output);
        Assert.Contains("\"status\":\"live\"", output);
    }

    [Fact]
    public async Task NotifyStreamEndedAsync_SendsStreamOfflineEvent()
    {
        var counter = new Counter { TwitchUserId = "user1" };

        await _notifier.NotifyStreamEndedAsync("user1", counter);

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: stream", output);
        Assert.Contains("\"status\":\"offline\"", output);
    }

    [Fact]
    public async Task NotifyFollowerAsync_SendsAlertEvent()
    {
        await _notifier.NotifyFollowerAsync("user1", "TestFollower");

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: alert", output);
        Assert.Contains("TestFollower", output);
    }

    [Fact]
    public async Task NotifySubscriberAsync_SendsAlertEvent()
    {
        await _notifier.NotifySubscriberAsync("user1", "TestSub", "1000", false);

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: alert", output);
        Assert.Contains("TestSub", output);
    }

    [Fact]
    public async Task NotifyBitsAsync_SendsAlertEvent()
    {
        await _notifier.NotifyBitsAsync("user1", "BitsDonor", 500, "bits!", 1000);

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: alert", output);
        Assert.Contains("BitsDonor", output);
        Assert.Contains("500", output);
    }

    [Fact]
    public async Task NotifyRaidAsync_SendsAlertEvent()
    {
        await _notifier.NotifyRaidAsync("user1", "Raider", 50);

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: alert", output);
        Assert.Contains("Raider", output);
    }

    [Fact]
    public async Task NotifyCustomAlertAsync_SendsAlertEventWithAlertType()
    {
        await _notifier.NotifyCustomAlertAsync("user1", "custom_event", new { msg = "hello" });

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: alert", output);
        Assert.Contains("custom_event", output);
    }

    [Fact]
    public async Task NotifyTemplateChangedAsync_SendsTemplateEvent()
    {
        await _notifier.NotifyTemplateChangedAsync("user1", "modern", new Template { Name = "Modern" });

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.Contains("event: template", output);
        Assert.Contains("modern", output);
    }

    [Fact]
    public async Task NotifyFollowerAsync_WithSuppressedAlert_DoesNotSend()
    {
        _mockAlertRepository.Setup(x => x.GetAlertsAsync("user1")).ReturnsAsync(new List<Alert>
        {
            new Alert { Type = "follow", IsEnabled = false }
        });

        await _notifier.NotifyFollowerAsync("user1", "SuppressedUser");

        var output = Encoding.UTF8.GetString(_clientStream.ToArray());
        Assert.DoesNotContain("event: alert", output);
    }
}
