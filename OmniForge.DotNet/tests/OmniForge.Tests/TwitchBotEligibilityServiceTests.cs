using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class TwitchBotEligibilityServiceTests
    {
        private static TwitchBotEligibilityService CreateService(
            Mock<ITwitchApiService> twitchApiService,
            Mock<IBotEligibilityCache> cache,
            string botUsername = "omniforge_bot")
        {
            var settings = Options.Create(new TwitchSettings { BotUsername = botUsername });
            var logger = new Mock<ILogger<TwitchBotEligibilityService>>();
            return new TwitchBotEligibilityService(twitchApiService.Object, settings, cache.Object, logger.Object);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenBroadcasterUserIdMissing_ReturnsNotEligible()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            var cache = new Mock<IBotEligibilityCache>();
            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync(" ", "token");

            Assert.False(result.UseBot);
            Assert.Equal("Missing broadcaster user id", result.Reason);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenAccessTokenMissing_ReturnsNotEligible()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            var cache = new Mock<IBotEligibilityCache>();
            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync("broadcaster1", " ");

            Assert.False(result.UseBot);
            Assert.Equal("Missing broadcaster access token", result.Reason);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenBotUsernameNotConfigured_ReturnsNotEligible()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            var cache = new Mock<IBotEligibilityCache>();
            var service = CreateService(twitchApiService, cache, botUsername: "");

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.False(result.UseBot);
            Assert.Equal("BotUsername is not configured", result.Reason);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenBotIsModerator_ShouldReturnUseBotTrue()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            twitchApiService
                .Setup(x => x.GetModeratorsAsync("broadcaster1", "token", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TwitchModeratorsResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    Moderators = new List<TwitchModeratorDto>
                    {
                        new TwitchModeratorDto { UserId = "bot-id", UserLogin = "omniforge_bot", UserName = "OmniForge Bot" }
                    }
                });
            var cache = new Mock<IBotEligibilityCache>();
            cache.Setup(x => x.TryGetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((BotEligibilityResult?)null);

            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.True(result.UseBot);
            Assert.Equal("bot-id", result.BotUserId);
            Assert.Equal("Bot is a moderator", result.Reason);

            cache.Verify(x => x.SetAsync(
                "broadcaster1",
                "omniforge_bot",
                It.Is<BotEligibilityResult>(r => r.UseBot && r.BotUserId == "bot-id"),
                It.Is<TimeSpan>(t => t == TimeSpan.FromHours(3)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenBotNotModerator_ShouldReturnUseBotFalse()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            twitchApiService
                .Setup(x => x.GetModeratorsAsync("broadcaster1", "token", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TwitchModeratorsResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    Moderators = new List<TwitchModeratorDto>
                    {
                        new TwitchModeratorDto { UserId = "someone-else", UserLogin = "other", UserName = "Other" }
                    }
                });
            var cache = new Mock<IBotEligibilityCache>();
            cache.Setup(x => x.TryGetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((BotEligibilityResult?)null);

            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.False(result.UseBot);
            Assert.Null(result.BotUserId);
            Assert.Equal("Bot is not a moderator in this channel", result.Reason);

            cache.Verify(x => x.SetAsync(
                "broadcaster1",
                "omniforge_bot",
                It.Is<BotEligibilityResult>(r => !r.UseBot),
                It.Is<TimeSpan>(t => t == TimeSpan.FromHours(3)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenForbidden_ShouldReturnScopeMessage()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            twitchApiService
                .Setup(x => x.GetModeratorsAsync("broadcaster1", "token", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TwitchModeratorsResponse
                {
                    StatusCode = HttpStatusCode.Forbidden,
                    Moderators = new List<TwitchModeratorDto>()
                });
            var cache = new Mock<IBotEligibilityCache>();
            cache.Setup(x => x.TryGetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((BotEligibilityResult?)null);

            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.False(result.UseBot);
            Assert.Contains("moderation:read", result.Reason);

            cache.Verify(x => x.SetAsync(
                "broadcaster1",
                "omniforge_bot",
                It.Is<BotEligibilityResult>(r => !r.UseBot),
                It.Is<TimeSpan>(t => t == TimeSpan.FromHours(3)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenUnauthorized_ShouldReturnNotEligible()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            twitchApiService
                .Setup(x => x.GetModeratorsAsync("broadcaster1", "token", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TwitchModeratorsResponse
                {
                    StatusCode = HttpStatusCode.Unauthorized,
                    Moderators = new List<TwitchModeratorDto>()
                });

            var cache = new Mock<IBotEligibilityCache>();
            cache.Setup(x => x.TryGetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((BotEligibilityResult?)null);

            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.False(result.UseBot);
            Assert.Contains("Unauthorized", result.Reason);

            cache.Verify(x => x.SetAsync(
                "broadcaster1",
                "omniforge_bot",
                It.Is<BotEligibilityResult>(r => !r.UseBot),
                It.Is<TimeSpan>(t => t == TimeSpan.FromHours(3)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenNonOkStatus_ShouldReturnNotEligible()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            twitchApiService
                .Setup(x => x.GetModeratorsAsync("broadcaster1", "token", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TwitchModeratorsResponse
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    Moderators = new List<TwitchModeratorDto>()
                });

            var cache = new Mock<IBotEligibilityCache>();
            cache.Setup(x => x.TryGetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((BotEligibilityResult?)null);

            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.False(result.UseBot);
            Assert.Contains("Failed to check moderators", result.Reason);

            cache.Verify(x => x.SetAsync(
                "broadcaster1",
                "omniforge_bot",
                It.Is<BotEligibilityResult>(r => !r.UseBot),
                It.Is<TimeSpan>(t => t == TimeSpan.FromHours(3)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenApiThrows_CachesShortTtlAndReturnsNotEligible()
        {
            var twitchApiService = new Mock<ITwitchApiService>();
            twitchApiService
                .Setup(x => x.GetModeratorsAsync("broadcaster1", "token", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var cache = new Mock<IBotEligibilityCache>();
            cache.Setup(x => x.TryGetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((BotEligibilityResult?)null);

            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.False(result.UseBot);
            Assert.Equal("Error checking moderators", result.Reason);

            cache.Verify(x => x.SetAsync(
                "broadcaster1",
                "omniforge_bot",
                It.Is<BotEligibilityResult>(r => !r.UseBot),
                It.Is<TimeSpan>(t => t == TimeSpan.FromSeconds(30)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetEligibilityAsync_WhenCached_ShouldNotCallHelix()
        {
            var twitchApiService = new Mock<ITwitchApiService>();

            var cache = new Mock<IBotEligibilityCache>();
            cache
                .Setup(x => x.TryGetAsync("broadcaster1", "omniforge_bot", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BotEligibilityResult(true, "bot-id", "cached"));

            var service = CreateService(twitchApiService, cache);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.True(result.UseBot);
            Assert.Equal("bot-id", result.BotUserId);
            twitchApiService.Verify(x => x.GetModeratorsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
