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

public class BotModerationServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<ITwitchApiService> _mockTwitchApiService;
    private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
    private readonly BotModerationService _sut;

    public BotModerationServiceTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockTwitchApiService = new Mock<ITwitchApiService>();
        _mockTwitchClientManager = new Mock<ITwitchClientManager>();

        // Build a real DI scope containing the mocked services
        var services = new ServiceCollection();
        services.AddScoped<IUserRepository>(_ => _mockUserRepository.Object);
        services.AddScoped<ITwitchApiService>(_ => _mockTwitchApiService.Object);
        services.AddScoped<ITwitchClientManager>(_ => _mockTwitchClientManager.Object);
        var provider = services.BuildServiceProvider();

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(provider);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _sut = new BotModerationService(NullLogger<BotModerationService>.Instance, mockScopeFactory.Object);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Skip mods and broadcasters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAndEnforce_SkipsMods_NoActionTaken()
    {
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "moduser", "msg1", "CAPS SPAM", isMod: true, isBroadcaster: false);

        _mockUserRepository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndEnforce_SkipsBroadcasters_NoActionTaken()
    {
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "thehost", "msg1", "CAPS SPAM", isMod: false, isBroadcaster: true);

        _mockUserRepository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndEnforce_NoBotModerationSettings_NoActionTaken()
    {
        // User not found → service returns early without any moderation actions
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync((User?)null);

        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1", "ALL CAPS MESSAGE HERE", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Anti-caps spam
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAndEnforce_AntiCapsDisabled_NoActionEvenWithAllCapsMessage()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    AntiCapsEnabled = false,
                    CapsPercentThreshold = 70,
                    CapsMinMessageLength = 5
                }
            });

        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "THIS IS ALL CAPS", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockTwitchApiService.Verify(s => s.BanUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndEnforce_AntiCapsEnabled_HighCapsPercent_DeletesAndBans()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    AntiCapsEnabled = true,
                    CapsPercentThreshold = 70,
                    CapsMinMessageLength = 5
                }
            });

        // 100% caps, 19 chars — above threshold and min length
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "THIS IS ALL CAPS!!!", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync("broadcaster1", "msg1"), Times.Once);
        _mockTwitchApiService.Verify(s => s.BanUserAsync("broadcaster1", "chatter1", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndEnforce_AntiCapsEnabled_MessageTooShort_NoAction()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    AntiCapsEnabled = true,
                    CapsPercentThreshold = 70,
                    CapsMinMessageLength = 20 // Require at least 20 chars
                }
            });

        // All caps but only 10 chars — should not trigger
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "HEY THERE!", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndEnforce_AntiCapsEnabled_LowCapsPercent_NoAction()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    AntiCapsEnabled = true,
                    CapsPercentThreshold = 70,
                    CapsMinMessageLength = 5
                }
            });

        // Mostly lowercase — should not trigger
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "hello this is lowercase text with only Some Caps", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Anti-symbol spam
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAndEnforce_AntiSymbolSpamDisabled_NoActionEvenWithHighSymbolCount()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    AntiSymbolSpamEnabled = false,
                    SymbolPercentThreshold = 50
                }
            });

        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "!!!!????####$$$$", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndEnforce_AntiSymbolSpamEnabled_HighSymbolPercent_DeletesAndBans()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    AntiSymbolSpamEnabled = true,
                    SymbolPercentThreshold = 50
                }
            });

        // All symbols — 100% symbol ratio
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "!!!!????####$$$$", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync("broadcaster1", "msg1"), Times.Once);
        _mockTwitchApiService.Verify(s => s.BanUserAsync("broadcaster1", "chatter1", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndEnforce_AntiSymbolSpamEnabled_LowSymbolPercent_NoAction()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    AntiSymbolSpamEnabled = true,
                    SymbolPercentThreshold = 50
                }
            });

        // Normal sentence with a few symbols
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "Hello! How are you? This is a normal message.", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Link guard
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAndEnforce_LinkGuardEnabled_DisallowedUrl_FirstViolation_DeletesAndWarns()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    LinkGuardEnabled = true,
                    AllowedDomains = new List<string> { "twitch.tv" }
                }
            });

        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "Check out badsite.com", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync("broadcaster1", "msg1"), Times.Once);
        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster1", It.Is<string>(m => m.Contains("chatterlogin"))), Times.Once);
        _mockTwitchApiService.Verify(s => s.BanUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndEnforce_LinkGuardEnabled_DisallowedUrl_SecondViolation_BansUser()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    LinkGuardEnabled = true,
                    AllowedDomains = new List<string>()
                }
            });

        // First violation
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "http://spam.com/promo", isMod: false, isBroadcaster: false);

        // Second violation
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg2",
            "http://spam2.com/promo", isMod: false, isBroadcaster: false);

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster1", It.IsAny<string>()), Times.Once);
        _mockTwitchApiService.Verify(s => s.BanUserAsync("broadcaster1", "chatter1", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndEnforce_LinkGuardEnabled_AllowedDomain_NoAction()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    LinkGuardEnabled = true,
                    AllowedDomains = new List<string> { "twitch.tv", "youtube.com" }
                }
            });

        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "Check the stream at https://twitch.tv/mychannel", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockTwitchApiService.Verify(s => s.MarkUserSuspiciousAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndEnforce_LinkGuardEnabled_MessageWithNoUrl_NoAction()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    LinkGuardEnabled = true,
                    AllowedDomains = new List<string>()
                }
            });

        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "This message has no links in it at all", isMod: false, isBroadcaster: false);

        _mockTwitchApiService.Verify(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ResetSession
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetSession_ClearsLinkViolationsForBroadcaster()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1"))
            .ReturnsAsync(new User
            {
                TwitchUserId = "broadcaster1",
                BotModeration = new BotModerationSettings
                {
                    LinkGuardEnabled = true,
                    AllowedDomains = new List<string>()
                }
            });

        // Accumulate one violation
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg1",
            "http://badsite.com", isMod: false, isBroadcaster: false);
        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster1", It.IsAny<string>()), Times.Once);

        // Reset session
        _sut.ResetSession("broadcaster1");

        // Post-reset: same chatter posts a link — strike count is 1 again, should warn (not ban)
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "chatterlogin", "msg2",
            "http://badsite.com", isMod: false, isBroadcaster: false);

        _mockTwitchClientManager.Verify(c => c.SendMessageAsync("broadcaster1", It.IsAny<string>()), Times.Exactly(2));
        _mockTwitchApiService.Verify(s => s.BanUserAsync("broadcaster1", "chatter1", It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResetSession_OnlyAffectsTargetBroadcaster()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new User
            {
                TwitchUserId = id,
                BotModeration = new BotModerationSettings
                {
                    LinkGuardEnabled = true,
                    AllowedDomains = new List<string>()
                }
            });

        // Accumulate violations for two different broadcasters
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "login1", "m1", "http://bad.com", false, false);
        await _sut.CheckAndEnforceAsync("broadcaster2", "chatter1", "login1", "m2", "http://bad.com", false, false);

        // Reset only broadcaster1
        _sut.ResetSession("broadcaster1");

        // chatter1 posts again in broadcaster2 — should ban (still has 1 violation from before reset)
        await _sut.CheckAndEnforceAsync("broadcaster2", "chatter1", "login1", "m3", "http://bad.com", false, false);
        _mockTwitchApiService.Verify(s => s.BanUserAsync("broadcaster2", "chatter1", It.IsAny<string>()), Times.Once);

        // chatter1 posts again in broadcaster1 — session was reset, should only mark suspicious
        await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "login1", "m4", "http://bad.com", false, false);
        _mockTwitchApiService.Verify(s => s.BanUserAsync("broadcaster1", "chatter1", It.IsAny<string>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // API failure resilience — catch blocks return false instead of throwing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAndEnforce_CapsSpam_ApiThrows_ReturnsFalse()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster1",
            Username = "broadcaster",
            BotModeration = new BotModerationSettings { AntiCapsEnabled = true, CapsPercentThreshold = 70, CapsMinMessageLength = 5 }
        });
        _mockTwitchApiService.Setup(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new System.Exception("API error"));

        var result = await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "login1", "msg1",
            "HELLO WORLD THIS IS CAPS", isMod: false, isBroadcaster: false);

        Assert.False(result);
    }

    [Fact]
    public async Task CheckAndEnforce_SymbolSpam_ApiThrows_ReturnsFalse()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster1",
            Username = "broadcaster",
            BotModeration = new BotModerationSettings { AntiSymbolSpamEnabled = true, SymbolPercentThreshold = 50 }
        });
        _mockTwitchApiService.Setup(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new System.Exception("API error"));

        var result = await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "login1", "msg1",
            "!!!!!!!!!!!!!!!!!!!!!!", isMod: false, isBroadcaster: false);

        Assert.False(result);
    }

    [Fact]
    public async Task CheckAndEnforce_LinkGuard_ApiThrows_ReturnsFalse()
    {
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster1",
            Username = "broadcaster",
            BotModeration = new BotModerationSettings { LinkGuardEnabled = true, AllowedDomains = new List<string>() }
        });
        _mockTwitchApiService.Setup(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new System.Exception("API error"));

        var result = await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "login1", "msg1",
            "check out http://malicious.com", isMod: false, isBroadcaster: false);

        Assert.False(result);
    }

    [Fact]
    public async Task CheckAndEnforce_LinkGuard_UnparseableUrl_TreatedAsDisallowed()
    {
        // A URL with an out-of-range port passes the URL regex but fails Uri.TryCreate —
        // the service should treat it as disallowed (return true after enforcing).
        _mockUserRepository.Setup(r => r.GetUserAsync("broadcaster1")).ReturnsAsync(new User
        {
            TwitchUserId = "broadcaster1",
            Username = "broadcaster",
            BotModeration = new BotModerationSettings { LinkGuardEnabled = true, AllowedDomains = new List<string>() }
        });
        _mockTwitchApiService.Setup(s => s.DeleteChatMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockTwitchClientManager.Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Port 99999 > 65535 — regex matches but Uri.TryCreate returns false
        var result = await _sut.CheckAndEnforceAsync("broadcaster1", "chatter1", "login1", "msg1",
            "check https://example.com:99999/path", isMod: false, isBroadcaster: false);

        Assert.True(result);
    }
}
