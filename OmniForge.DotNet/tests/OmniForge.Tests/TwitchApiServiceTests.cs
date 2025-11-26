using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Services;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using Xunit;

namespace OmniForge.Tests
{
    public class TwitchApiServiceTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ITwitchAuthService> _mockAuthService;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<ITwitchHelixWrapper> _mockHelixWrapper;
        private readonly Mock<ILogger<TwitchApiService>> _mockLogger;
        private readonly TwitchApiService _service;

        public TwitchApiServiceTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockAuthService = new Mock<ITwitchAuthService>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockHelixWrapper = new Mock<ITwitchHelixWrapper>();
            _mockLogger = new Mock<ILogger<TwitchApiService>>();

            _mockConfiguration.Setup(x => x["Twitch:ClientId"]).Returns("test_client_id");

            _service = new TwitchApiService(
                _mockUserRepository.Object,
                _mockAuthService.Object,
                _mockConfiguration.Object,
                _mockHelixWrapper.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldReturnRewards_WhenUserExists()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "access_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var rewards = new List<HelixCustomReward>
            {
                new HelixCustomReward { Id = "r1", Title = "Reward 1", Cost = 100, IsEnabled = true }
            };

            _mockHelixWrapper.Setup(x => x.GetCustomRewardsAsync("test_client_id", "access_token", userId))
                .ReturnsAsync(rewards);

            var result = await _service.GetCustomRewardsAsync(userId);

            Assert.Single(result);
            Assert.Equal("r1", result.First().Id);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldRefreshToken_WhenExpired()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "old_token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddMinutes(-1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var newToken = new TwitchTokenResponse
            {
                AccessToken = "new_token",
                RefreshToken = "new_refresh",
                ExpiresIn = 3600
            };

            _mockAuthService.Setup(x => x.RefreshTokenAsync("refresh_token")).ReturnsAsync(newToken);

            var rewards = new List<HelixCustomReward>();

            _mockHelixWrapper.Setup(x => x.GetCustomRewardsAsync("test_client_id", "new_token", userId))
                .ReturnsAsync(rewards);

            await _service.GetCustomRewardsAsync(userId);

            _mockAuthService.Verify(x => x.RefreshTokenAsync("refresh_token"), Times.Once);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.AccessToken == "new_token")), Times.Once);
        }

        [Fact]
        public async Task CreateCustomRewardAsync_ShouldCreateReward_WhenValid()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "access_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var request = new CreateRewardRequest
            {
                Title = "New Reward",
                Cost = 500
            };

            var createdReward = new HelixCustomReward
            {
                Id = "new_r",
                Title = "New Reward",
                Cost = 500,
                IsEnabled = true
            };

            _mockHelixWrapper.Setup(x => x.CreateCustomRewardAsync(
                "test_client_id",
                "access_token",
                userId,
                It.Is<CreateCustomRewardsRequest>(r => r.Title == "New Reward")))
                .ReturnsAsync(new List<HelixCustomReward> { createdReward });

            var result = await _service.CreateCustomRewardAsync(userId, request);

            Assert.NotNull(result);
            Assert.Equal("new_r", result.Id);
            Assert.Equal("New Reward", result.Title);
        }
    }
}
