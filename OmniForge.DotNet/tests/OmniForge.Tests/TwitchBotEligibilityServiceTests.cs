using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class TwitchBotEligibilityServiceTests
    {
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

            var settings = Options.Create(new TwitchSettings { BotUsername = "omniforge_bot" });
            var logger = new Mock<ILogger<TwitchBotEligibilityService>>();

            var service = new TwitchBotEligibilityService(twitchApiService.Object, settings, logger.Object);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.True(result.UseBot);
            Assert.Equal("bot-id", result.BotUserId);
            Assert.Equal("Bot is a moderator", result.Reason);
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

            var settings = Options.Create(new TwitchSettings { BotUsername = "omniforge_bot" });
            var logger = new Mock<ILogger<TwitchBotEligibilityService>>();

            var service = new TwitchBotEligibilityService(twitchApiService.Object, settings, logger.Object);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.False(result.UseBot);
            Assert.Null(result.BotUserId);
            Assert.Equal("Bot is not a moderator in this channel", result.Reason);
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

            var settings = Options.Create(new TwitchSettings { BotUsername = "omniforge_bot" });
            var logger = new Mock<ILogger<TwitchBotEligibilityService>>();

            var service = new TwitchBotEligibilityService(twitchApiService.Object, settings, logger.Object);

            var result = await service.GetEligibilityAsync("broadcaster1", "token");

            Assert.False(result.UseBot);
            Assert.Contains("moderation:read", result.Reason);
        }
    }
}
