using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Services;

public class BotReactionServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
    private readonly BotReactionService _sut;

    public BotReactionServiceTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockTwitchClientManager = new Mock<ITwitchClientManager>();

        var services = new ServiceCollection();
        services.AddScoped<IUserRepository>(_ => _mockUserRepository.Object);
        services.AddScoped<ITwitchClientManager>(_ => _mockTwitchClientManager.Object);
        var provider = services.BuildServiceProvider();

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(provider);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _sut = new BotReactionService(NullLogger<BotReactionService>.Instance, mockScopeFactory.Object);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // User not found / settings null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleStreamStartAsync_UserNotFound_DoesNotSendMessage()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync((User?)null);

        await _sut.HandleStreamStartAsync("broadcaster-1");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleStreamStartAsync_BotSettingsNull_DoesNotSendMessage()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = null!
        });

        await _sut.HandleStreamStartAsync("broadcaster-1");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HandleStreamStartAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleStreamStartAsync_NoMessage_DoesNotSend()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { StreamStartMessage = null }
        });

        await _sut.HandleStreamStartAsync("broadcaster-1");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleStreamStartAsync_HasMessage_SendsMessage()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { StreamStartMessage = "Stream is live!" }
        });

        await _sut.HandleStreamStartAsync("broadcaster-1");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster-1", "Stream is live!"), Times.Once);
    }

    [Fact]
    public async Task HandleStreamStartAsync_WhitespaceOnlyMessage_DoesNotSend()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { StreamStartMessage = "   " }
        });

        await _sut.HandleStreamStartAsync("broadcaster-1");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HandleRaidReceivedAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleRaidReceivedAsync_ReplacesRaiderAndViewersTokens()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { RaidReceivedMessage = "Welcome {raider} with {viewers} viewers!" }
        });

        await _sut.HandleRaidReceivedAsync("broadcaster-1", "RaiderUser", 42);

        _mockTwitchClientManager.Verify(
            c => c.SendMessageAsync("broadcaster-1", "Welcome RaiderUser with 42 viewers!"),
            Times.Once);
    }

    [Fact]
    public async Task HandleRaidReceivedAsync_NoMessage_DoesNotSend()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { RaidReceivedMessage = null }
        });

        await _sut.HandleRaidReceivedAsync("broadcaster-1", "RaiderUser", 42);

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HandleNewSubAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleNewSubAsync_ReplacesUsernameAndTierTokens()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { NewSubMessage = "Thanks {username} for subscribing at tier {tier}!" }
        });

        await _sut.HandleNewSubAsync("broadcaster-1", "SubUser", "Tier 1");

        _mockTwitchClientManager.Verify(
            c => c.SendMessageAsync("broadcaster-1", "Thanks SubUser for subscribing at tier Tier 1!"),
            Times.Once);
    }

    [Fact]
    public async Task HandleNewSubAsync_NoMessage_DoesNotSend()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { NewSubMessage = null }
        });

        await _sut.HandleNewSubAsync("broadcaster-1", "SubUser", "Tier 1");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HandleGiftSubAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleGiftSubAsync_ReplacesGifterAndCountTokens()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { GiftSubMessage = "{gifter} gifted {count} subs!" }
        });

        await _sut.HandleGiftSubAsync("broadcaster-1", "GifterUser", 5);

        _mockTwitchClientManager.Verify(
            c => c.SendMessageAsync("broadcaster-1", "GifterUser gifted 5 subs!"),
            Times.Once);
    }

    [Fact]
    public async Task HandleGiftSubAsync_NoMessage_DoesNotSend()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { GiftSubMessage = null }
        });

        await _sut.HandleGiftSubAsync("broadcaster-1", "GifterUser", 5);

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HandleResubAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleResubAsync_ReplacesUsernameAndMonthsTokens()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { ResubMessage = "{username} has resubbed for {months} months!" }
        });

        await _sut.HandleResubAsync("broadcaster-1", "ResubUser", 12);

        _mockTwitchClientManager.Verify(
            c => c.SendMessageAsync("broadcaster-1", "ResubUser has resubbed for 12 months!"),
            Times.Once);
    }

    [Fact]
    public async Task HandleResubAsync_NoMessage_DoesNotSend()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { ResubMessage = null }
        });

        await _sut.HandleResubAsync("broadcaster-1", "ResubUser", 12);

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HandleFirstTimeChatAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleFirstTimeChatAsync_FirstMessage_SendsGreeting()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { FirstTimeChatMessage = "Welcome {username}, first time in chat!" }
        });

        await _sut.HandleFirstTimeChatAsync("broadcaster-1", "chatter-1", "ChatterDisplay");

        _mockTwitchClientManager.Verify(
            c => c.SendMessageAsync("broadcaster-1", "Welcome ChatterDisplay, first time in chat!"),
            Times.Once);
    }

    [Fact]
    public async Task HandleFirstTimeChatAsync_SecondCallSameChatter_SkipsGreeting()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { FirstTimeChatMessage = "Welcome {username}!" }
        });

        await _sut.HandleFirstTimeChatAsync("broadcaster-1", "chatter-1", "ChatterDisplay");
        await _sut.HandleFirstTimeChatAsync("broadcaster-1", "chatter-1", "ChatterDisplay");

        _mockTwitchClientManager.Verify(
            c => c.SendMessageAsync("broadcaster-1", It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleFirstTimeChatAsync_NoMessage_DoesNotSend()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { FirstTimeChatMessage = null }
        });

        await _sut.HandleFirstTimeChatAsync("broadcaster-1", "chatter-1", "ChatterDisplay");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HandleClipCreatedAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleClipCreatedAsync_ReplacesUrlToken()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { ClipAnnouncementMessage = "New clip: {url}" }
        });

        await _sut.HandleClipCreatedAsync("broadcaster-1", "https://clips.twitch.tv/abc123");

        _mockTwitchClientManager.Verify(
            c => c.SendMessageAsync("broadcaster-1", "New clip: https://clips.twitch.tv/abc123"),
            Times.Once);
    }

    [Fact]
    public async Task HandleClipCreatedAsync_NoMessage_DoesNotSend()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { ClipAnnouncementMessage = null }
        });

        await _sut.HandleClipCreatedAsync("broadcaster-1", "https://clips.twitch.tv/abc123");

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ResetSession
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetSession_ClearsFirstTimeChatState_AllowsGreetAgain()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster-1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster-1",
            Username = "testuser",
            BotSettings = new BotSettings { FirstTimeChatMessage = "Welcome {username}!" }
        });

        await _sut.HandleFirstTimeChatAsync("broadcaster-1", "chatter-1", "ChatterDisplay");

        _sut.ResetSession("broadcaster-1");

        await _sut.HandleFirstTimeChatAsync("broadcaster-1", "chatter-1", "ChatterDisplay");

        _mockTwitchClientManager.Verify(
            c => c.SendMessageAsync("broadcaster-1", It.IsAny<string>()),
            Times.Exactly(2));
    }

    [Fact]
    public void ResetSession_UnknownBroadcaster_DoesNotThrow()
    {
        var exception = Record.Exception(() => _sut.ResetSession("unknown-broadcaster"));
        Assert.Null(exception);
    }
}
