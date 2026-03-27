using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Services;

public class ScheduledMessageServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScheduledMessageService _sut;

    public ScheduledMessageServiceTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockTwitchClientManager = new Mock<ITwitchClientManager>();

        var services = new ServiceCollection();
        services.AddScoped<IUserRepository>(_ => _mockUserRepository.Object);
        var provider = services.BuildServiceProvider();

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(provider);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _scopeFactory = mockScopeFactory.Object;

        _sut = new ScheduledMessageService(
            NullLogger<ScheduledMessageService>.Instance,
            _scopeFactory,
            _mockTwitchClientManager.Object);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StartForUser / StopForUser lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StartForUser_DoesNotThrow()
    {
        var exception = Record.Exception(() => _sut.StartForUser("broadcaster-1"));
        Assert.Null(exception);
        // Clean up the timer
        _sut.StopForUser("broadcaster-1");
    }

    [Fact]
    public void StopForUser_AfterStart_DoesNotThrow()
    {
        _sut.StartForUser("broadcaster-1");
        var exception = Record.Exception(() => _sut.StopForUser("broadcaster-1"));
        Assert.Null(exception);
    }

    [Fact]
    public void StopForUser_UnknownBroadcaster_DoesNotThrow()
    {
        var exception = Record.Exception(() => _sut.StopForUser("unknown-broadcaster"));
        Assert.Null(exception);
    }

    [Fact]
    public void StartForUser_CalledTwice_ReplacesTimer_DoesNotThrow()
    {
        _sut.StartForUser("broadcaster-1");
        var exception = Record.Exception(() => _sut.StartForUser("broadcaster-1"));
        Assert.Null(exception);
        _sut.StopForUser("broadcaster-1");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TickAsync — driven via reflection since the method is private
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TickAsync_SendsEnabledMessages_WhenIntervalElapsed()
    {
        var msgId = Guid.NewGuid().ToString();
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings
            {
                ScheduledMessages = new List<ScheduledMessageEntry>
                {
                    new ScheduledMessageEntry
                    {
                        Id = msgId,
                        Message = "Hello chat!",
                        IntervalMinutes = 0, // 0 minutes — always elapsed
                        Enabled = true
                    }
                }
            }
        });

        // Use a very short dueTime so the timer fires quickly during the test
        // We use a TaskCompletionSource to gate on the first send.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockTwitchClientManager
            .Setup(c => c.SendMessageAsync("broadcaster-1", "Hello chat!"))
            .Callback(() => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        // Start the service with its 1-minute timer — that's too slow for a unit test.
        // We call StartForUser to set up the semaphore and _lastFired, then directly
        // invoke the observable path by constructing a local timer at 10 ms.
        _sut.StartForUser("broadcaster-1");

        // Simulate what the real timer callback does: acquire sem, call TickAsync.
        // We can't call private TickAsync, but we can prove the end-to-end path by
        // replacing with a short-lived timer internally, which means inspecting the
        // timer through time. Since we can't do that without reflection, use a
        // parallel Timer that fires into the same logic path.
        // The pragmatic unit-test solution: use reflection once to call TickAsync.
        var tickMethod = typeof(ScheduledMessageService)
            .GetMethod("TickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(tickMethod);

        await (Task)tickMethod!.Invoke(_sut, new object[] { "broadcaster-1" })!;

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster-1", "Hello chat!"), Times.Once);

        _sut.StopForUser("broadcaster-1");
    }

    [Fact]
    public async Task TickAsync_SkipsDisabledMessages()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings
            {
                ScheduledMessages = new List<ScheduledMessageEntry>
                {
                    new ScheduledMessageEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Message = "This should not be sent",
                        IntervalMinutes = 0,
                        Enabled = false
                    }
                }
            }
        });

        _sut.StartForUser("broadcaster-1");

        var tickMethod = typeof(ScheduledMessageService)
            .GetMethod("TickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)tickMethod!.Invoke(_sut, new object[] { "broadcaster-1" })!;

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        _sut.StopForUser("broadcaster-1");
    }

    [Fact]
    public async Task TickAsync_SkipsMessageWhenIntervalNotElapsed()
    {
        var msgId = Guid.NewGuid().ToString();
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings
            {
                ScheduledMessages = new List<ScheduledMessageEntry>
                {
                    new ScheduledMessageEntry
                    {
                        Id = msgId,
                        Message = "Interval message",
                        IntervalMinutes = 60, // 60-minute interval — not elapsed after first fire
                        Enabled = true
                    }
                }
            }
        });

        _sut.StartForUser("broadcaster-1");

        var tickMethod = typeof(ScheduledMessageService)
            .GetMethod("TickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // First tick — interval elapsed from MinValue, so message fires
        await (Task)tickMethod!.Invoke(_sut, new object[] { "broadcaster-1" })!;

        // Second tick immediately after — 60-minute interval has not elapsed
        await (Task)tickMethod!.Invoke(_sut, new object[] { "broadcaster-1" })!;

        // Should have been sent exactly once
        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster-1", "Interval message"), Times.Once);

        _sut.StopForUser("broadcaster-1");
    }

    [Fact]
    public async Task TickAsync_UserNotFound_DoesNotThrow()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync((User?)null);

        _sut.StartForUser("broadcaster-1");

        var tickMethod = typeof(ScheduledMessageService)
            .GetMethod("TickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var exception = await Record.ExceptionAsync(
            () => (Task)tickMethod!.Invoke(_sut, new object[] { "broadcaster-1" })!);

        Assert.Null(exception);

        _sut.StopForUser("broadcaster-1");
    }

    [Fact]
    public async Task TickAsync_EmptyMessage_SkipsEntry()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings
            {
                ScheduledMessages = new List<ScheduledMessageEntry>
                {
                    new ScheduledMessageEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Message = "   ",  // whitespace only
                        IntervalMinutes = 0,
                        Enabled = true
                    }
                }
            }
        });

        _sut.StartForUser("broadcaster-1");

        var tickMethod = typeof(ScheduledMessageService)
            .GetMethod("TickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)tickMethod!.Invoke(_sut, new object[] { "broadcaster-1" })!;

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        _sut.StopForUser("broadcaster-1");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StopForUser clears _lastFired
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopForUser_ClearsLastFired_SoNextStartResetsIntervalState()
    {
        var msgId = Guid.NewGuid().ToString();
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings
            {
                ScheduledMessages = new List<ScheduledMessageEntry>
                {
                    new ScheduledMessageEntry
                    {
                        Id = msgId,
                        Message = "Interval message",
                        IntervalMinutes = 60,
                        Enabled = true
                    }
                }
            }
        });

        var tickMethod = typeof(ScheduledMessageService)
            .GetMethod("TickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        _sut.StartForUser("broadcaster-1");

        // First tick — message fires, sets lastFired to now
        await (Task)tickMethod!.Invoke(_sut, new object[] { "broadcaster-1" })!;

        // Stop — clears _lastFired
        _sut.StopForUser("broadcaster-1");

        // Restart — _lastFired is gone, so interval is elapsed again
        _sut.StartForUser("broadcaster-1");
        await (Task)tickMethod!.Invoke(_sut, new object[] { "broadcaster-1" })!;

        // Message should have been sent twice total (once per StartForUser cycle)
        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster-1", "Interval message"), Times.Exactly(2));

        _sut.StopForUser("broadcaster-1");
    }
}
