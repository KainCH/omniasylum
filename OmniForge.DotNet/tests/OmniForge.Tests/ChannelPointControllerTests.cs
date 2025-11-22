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
    }
}
