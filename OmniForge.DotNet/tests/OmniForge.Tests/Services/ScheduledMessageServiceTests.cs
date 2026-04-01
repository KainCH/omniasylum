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
    // ExecuteTickAsync — semaphore paths
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteTickAsync_WhenNoSemaphoreRegistered_ReturnsEarly()
    {
        // No StartForUser called — _tickLocks has no entry for this broadcasterId
        var exception = await Record.ExceptionAsync(() => _sut.ExecuteTickAsync("unknown-broadcaster"));
        Assert.Null(exception);
        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteTickAsync_WhenSemaphoreAlreadyAcquired_SkipsTick()
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
                        Message = "Hello chat!",
                        IntervalMinutes = 1,
                        Enabled = true
                    }
                }
            }
        });

        _sut.StartForUser("broadcaster-1");

        // Manually hold the semaphore so ExecuteTickAsync sees acquired = false
        var semField = typeof(ScheduledMessageService)
            .GetField("_tickLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tickLocks = (System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>)semField.GetValue(_sut)!;
        tickLocks.TryGetValue("broadcaster-1", out var sem);
        await sem!.WaitAsync(); // hold the semaphore

        try
        {
            await _sut.ExecuteTickAsync("broadcaster-1");
            // Tick was skipped — no message sent
            _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            sem.Release();
            _sut.StopForUser("broadcaster-1");
        }
    }

    [Fact]
    public async Task ExecuteTickAsync_AfterStopForUser_HandlesObjectDisposedException()
    {
        _sut.StartForUser("broadcaster-1");

        // Get reference to the semaphore before stopping
        var semField = typeof(ScheduledMessageService)
            .GetField("_tickLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tickLocks = (System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>)semField.GetValue(_sut)!;
        tickLocks.TryGetValue("broadcaster-1", out var sem);

        // Stop disposes the semaphore and removes from _tickLocks
        _sut.StopForUser("broadcaster-1");

        // Re-insert the disposed semaphore to exercise the ObjectDisposedException path
        tickLocks["broadcaster-1"] = sem!;

        var exception = await Record.ExceptionAsync(() => _sut.ExecuteTickAsync("broadcaster-1"));
        Assert.Null(exception);

        // Cleanup
        tickLocks.TryRemove("broadcaster-1", out _);
    }

    [Fact]
    public async Task ExecuteTickAsync_HappyPath_AcquiresSemaphoreAndRunsTick()
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
                        IntervalMinutes = 0,
                        Enabled = true
                    }
                }
            }
        });
        _mockTwitchClientManager
            .Setup(c => c.SendMessageAsync("broadcaster-1", "Hello chat!"))
            .Returns(Task.CompletedTask);

        _sut.StartForUser("broadcaster-1");
        await _sut.ExecuteTickAsync("broadcaster-1");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster-1", "Hello chat!"), Times.Once);

        _sut.StopForUser("broadcaster-1");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TickAsync — driven via ExecuteTickAsync (no reflection needed for TickAsync)
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
                        IntervalMinutes = 0, // 0 minutes — clamped to 1, but _lastFired is MinValue so always elapsed
                        Enabled = true
                    }
                }
            }
        });

        _mockTwitchClientManager
            .Setup(c => c.SendMessageAsync("broadcaster-1", "Hello chat!"))
            .Returns(Task.CompletedTask);

        _sut.StartForUser("broadcaster-1");
        await _sut.ExecuteTickAsync("broadcaster-1");

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
        await _sut.ExecuteTickAsync("broadcaster-1");

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

        // First tick — interval elapsed from MinValue, so message fires
        await _sut.ExecuteTickAsync("broadcaster-1");

        // Second tick immediately after — 60-minute interval has not elapsed
        await _sut.ExecuteTickAsync("broadcaster-1");

        // Should have been sent exactly once
        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster-1", "Interval message"), Times.Once);

        _sut.StopForUser("broadcaster-1");
    }

    [Fact]
    public async Task TickAsync_UserNotFound_DoesNotThrow()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync((User?)null);

        _sut.StartForUser("broadcaster-1");

        var exception = await Record.ExceptionAsync(() => _sut.ExecuteTickAsync("broadcaster-1"));

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
        await _sut.ExecuteTickAsync("broadcaster-1");

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

        _sut.StartForUser("broadcaster-1");

        // First tick — message fires, sets lastFired to now
        await _sut.ExecuteTickAsync("broadcaster-1");

        // Stop — clears _lastFired
        _sut.StopForUser("broadcaster-1");

        // Restart — _lastFired is gone, so interval is elapsed again
        _sut.StartForUser("broadcaster-1");
        await _sut.ExecuteTickAsync("broadcaster-1");

        // Message should have been sent twice total (once per StartForUser cycle)
        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster-1", "Interval message"), Times.Exactly(2));

        _sut.StopForUser("broadcaster-1");
    }
}
