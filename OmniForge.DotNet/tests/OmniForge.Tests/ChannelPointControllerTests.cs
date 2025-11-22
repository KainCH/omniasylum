using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class ChannelPointControllerTests
    {
        private readonly Mock<ITwitchApiService> _mockTwitchApiService;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IChannelPointRepository> _mockChannelPointRepository;
        private readonly ChannelPointController _controller;

        public ChannelPointControllerTests()
        {
            _mockTwitchApiService = new Mock<ITwitchApiService>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockChannelPointRepository = new Mock<IChannelPointRepository>();
            _controller = new ChannelPointController(
                _mockTwitchApiService.Object,
                _mockUserRepository.Object,
                _mockChannelPointRepository.Object);

            // Setup User context
            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        [Fact]
        public async Task CreateReward_ShouldReturnBadRequest_WhenCostIsTooHigh()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = true } });

            var request = new CreateRewardRequest
            {
                Title = "Test Reward",
                Cost = 1000001,
                Action = "increment_deaths"
            };

            var result = await _controller.CreateReward(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Cost must be between 1 and 1,000,000", badRequestResult.Value);
        }

        [Fact]
        public async Task CreateReward_ShouldReturnBadRequest_WhenActionIsInvalid()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = true } });

            var request = new CreateRewardRequest
            {
                Title = "Test Reward",
                Cost = 100,
                Action = "invalid_action"
            };

            var result = await _controller.CreateReward(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid action type", badRequestResult.Value);
        }

        [Fact]
        public async Task CreateReward_ShouldReturnOk_WhenValid()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = true } });

            var request = new CreateRewardRequest
            {
                Title = "Test Reward",
                Cost = 100,
                Action = "increment_deaths"
            };

            var twitchReward = new TwitchCustomReward
            {
                Id = "reward1",
                Title = "Test Reward",
                Cost = 100,
                IsEnabled = true
            };

            _mockTwitchApiService.Setup(x => x.CreateCustomRewardAsync("12345", request))
                .ReturnsAsync(twitchReward);

            var result = await _controller.CreateReward(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockChannelPointRepository.Verify(x => x.SaveRewardAsync(It.IsAny<ChannelPointReward>()), Times.Once);
        }

        [Fact]
        public async Task GetRewards_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.GetRewards();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task GetRewards_ShouldReturnForbid_WhenFeatureDisabled()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = false } });

            var result = await _controller.GetRewards();

            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Contains("Channel points feature not enabled", forbidResult.AuthenticationSchemes);
        }

        [Fact]
        public async Task GetRewards_ShouldReturnOk_WhenSuccessful()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = true } });

            var rewards = new List<ChannelPointReward>
            {
                new ChannelPointReward { RewardId = "1", RewardTitle = "Reward 1" }
            };

            _mockChannelPointRepository.Setup(x => x.GetRewardsAsync("12345"))
                .ReturnsAsync(rewards);

            var result = await _controller.GetRewards();

            var okResult = Assert.IsType<OkObjectResult>(result);
            // Use reflection or dynamic to check anonymous type properties if needed,
            // or just verify the repository call.
            _mockChannelPointRepository.Verify(x => x.GetRewardsAsync("12345"), Times.Once);
        }

        [Fact]
        public async Task GetRewards_ShouldReturn500_WhenExceptionOccurs()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = true } });

            _mockChannelPointRepository.Setup(x => x.GetRewardsAsync("12345"))
                .ThrowsAsync(new Exception("DB Error"));

            var result = await _controller.GetRewards();

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task DeleteReward_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.DeleteReward("reward1");

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task DeleteReward_ShouldReturnForbid_WhenFeatureDisabled()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = false } });

            var result = await _controller.DeleteReward("reward1");

            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Contains("Channel points feature not enabled", forbidResult.AuthenticationSchemes);
        }

        [Fact]
        public async Task DeleteReward_ShouldReturnOk_WhenSuccessful()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = true } });

            var result = await _controller.DeleteReward("reward1");

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockTwitchApiService.Verify(x => x.DeleteCustomRewardAsync("12345", "reward1"), Times.Once);
            _mockChannelPointRepository.Verify(x => x.DeleteRewardAsync("12345", "reward1"), Times.Once);
        }

        [Fact]
        public async Task DeleteReward_ShouldReturn500_WhenExceptionOccurs()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { Features = new FeatureFlags { ChannelPoints = true } });

            _mockTwitchApiService.Setup(x => x.DeleteCustomRewardAsync("12345", "reward1"))
                .ThrowsAsync(new Exception("API Error"));

            var result = await _controller.DeleteReward("reward1");

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }
    }
}
