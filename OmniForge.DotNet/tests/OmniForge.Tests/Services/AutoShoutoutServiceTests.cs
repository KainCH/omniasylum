using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Services;

public class AutoShoutoutServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<ITwitchApiService> _mockTwitchApiService;
    private readonly AutoShoutoutService _sut;

    public AutoShoutoutServiceTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockTwitchApiService = new Mock<ITwitchApiService>();

        var services = new ServiceCollection();
        services.AddScoped<IUserRepository>(_ => _mockUserRepository.Object);
        services.AddScoped<ITwitchApiService>(_ => _mockTwitchApiService.Object);
        var provider = services.BuildServiceProvider();

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(provider);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _sut = new AutoShoutoutService(NullLogger<AutoShoutoutService>.Instance, mockScopeFactory.Object);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Broadcaster / mod early-return
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleChatMessageAsync_BroadcasterFlag_ReturnsEarlyWithoutUserLookup()
    {
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: true);

        _mockUserRepository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleChatMessageAsync_ModFlag_ReturnsEarlyWithoutUserLookup()
    {
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: true, isBroadcaster: false);

        _mockUserRepository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // User not found
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleChatMessageAsync_UserNotFound_ReturnsEarlyWithoutShoutout()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync((User?)null);

        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Exclusion list
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleChatMessageAsync_ChatterInExcludeList_ReturnsEarlyWithoutShoutout()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string> { "excludedlogin" }
        });

        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "excludedlogin", "ExcludedDisplay",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.IsFollowingAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleChatMessageAsync_ExcludeListMatchIsCaseInsensitive()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string> { "ExcludedLogin" }
        });

        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "excludedlogin", "ExcludedDisplay",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Follow cache — not a follower
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleChatMessageAsync_ChatterNotFollowing_NoShoutoutSent()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string>()
        });
        _mockTwitchApiService.Setup(s => s.IsFollowingAsync("broadcaster-1", "chatter-1")).ReturnsAsync(false);

        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Shoutout sent on first message from a follower
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleChatMessageAsync_FollowerFirstMessage_ShoutoutSent()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string>()
        });
        _mockTwitchApiService.Setup(s => s.IsFollowingAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);
        _mockTwitchApiService.Setup(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);

        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1"), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Second message in same session is skipped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleChatMessageAsync_SameChatterSecondMessage_ShoutoutSkipped()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string>()
        });
        _mockTwitchApiService.Setup(s => s.IsFollowingAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);
        _mockTwitchApiService.Setup(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);

        // First message — shoutout should fire
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);

        // Second message in same session — should be skipped due to session set
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1"), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Follow cache hit
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleChatMessageAsync_SessionDedup_SecondCallForSameChatter_DoesNotRecheckFollowApi()
    {
        // Session dedup (not follow cache) is now the primary gate that prevents duplicate shoutouts.
        // Once a chatter is shouted, the session set blocks any further calls before the API is reached.
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string>()
        });
        _mockTwitchApiService.Setup(s => s.IsFollowingAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);
        _mockTwitchApiService.Setup(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);

        // First call — populates session set and sends shoutout
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);

        // Second call — session dedup blocks before IsFollowingAsync is reached
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.IsFollowingAsync("broadcaster-1", "chatter-1"), Times.Once);
        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1"), Times.Once);
    }

    [Fact]
    public async Task HandleChatMessageAsync_FollowCacheHit_NotFollower_NoShoutout()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string>()
        });
        _mockTwitchApiService.Setup(s => s.IsFollowingAsync("broadcaster-1", "chatter-1")).ReturnsAsync(false);

        // Two calls — API should only be hit once due to cache
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.IsFollowingAsync("broadcaster-1", "chatter-1"), Times.Once);
        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Channel cooldown
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleChatMessageAsync_ChannelCooldownActive_SecondChatterSkipped()
    {
        // chatter-1 and chatter-2 are different users; after chatter-1 fires a shoutout the channel
        // cooldown should block chatter-2 from immediately getting one.
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string>()
        });
        _mockTwitchApiService.Setup(s => s.IsFollowingAsync("broadcaster-1", It.IsAny<string>())).ReturnsAsync(true);
        _mockTwitchApiService.Setup(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);
        _mockTwitchApiService.Setup(s => s.SendShoutoutAsync("broadcaster-1", "chatter-2")).ReturnsAsync(true);

        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "login1", "Display1",
            isMod: false, isBroadcaster: false);

        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-2", "login2", "Display2",
            isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1"), Times.Once);
        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync("broadcaster-1", "chatter-2"), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ResetSession
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetSession_ClearsAllState_AllowsFullShoutoutCycleAgain()
    {
        // ResetSession now clears session set, cooldowns, and follow cache — simulating stream restart.
        // A chatter who was shouted in the previous stream should be shouted again in the new stream.
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            AutoShoutoutExcludeList = new List<string>()
        });
        _mockTwitchApiService.Setup(s => s.IsFollowingAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);
        _mockTwitchApiService.Setup(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1")).ReturnsAsync(true);

        // First stream — chatter gets shouted
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);
        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1"), Times.Once);

        // Stream ends — full session reset clears session set, cooldowns, and follow cache
        _sut.ResetSession("broadcaster-1");

        // Second stream — all state cleared, chatter should be eligible for shoutout again
        await _sut.HandleChatMessageAsync("broadcaster-1", "chatter-1", "chatterlogin", "ChatterDisplay",
            isMod: false, isBroadcaster: false);
        _mockTwitchApiService.Verify(s => s.SendShoutoutAsync("broadcaster-1", "chatter-1"), Times.Exactly(2));
    }

    [Fact]
    public void ResetSession_UnknownBroadcaster_DoesNotThrow()
    {
        var exception = Record.Exception(() => _sut.ResetSession("unknown-broadcaster"));
        Assert.Null(exception);
    }
}
