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
    public class AlertControllerTests
    {
        private readonly Mock<IAlertRepository> _mockAlertRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly AlertController _controller;

        public AlertControllerTests()
        {
            _mockAlertRepository = new Mock<IAlertRepository>();
            _mockUserRepository = new Mock<IUserRepository>();

            _controller = new AlertController(
                _mockAlertRepository.Object,
                _mockUserRepository.Object);

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
        public async Task GetAlerts_ShouldReturnOk_WhenUserExists()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { StreamAlerts = true } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockAlertRepository.Setup(x => x.GetAlertsAsync("12345")).ReturnsAsync(new List<Alert>());

            var result = await _controller.GetAlerts();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task CreateAlert_ShouldReturnBadRequest_WhenInvalidData()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { StreamAlerts = true } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new CreateAlertRequest { Type = "", Name = "Test" }; // Missing Type

            var result = await _controller.CreateAlert(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CreateAlert_ShouldReturnOk_WhenValidData()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { StreamAlerts = true } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new CreateAlertRequest 
            { 
                Type = "follow", 
                Name = "Test Alert", 
                TextPrompt = "New Follower!" 
            };

            var result = await _controller.CreateAlert(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockAlertRepository.Verify(x => x.SaveAlertAsync(It.IsAny<Alert>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAlert_ShouldReturnNotFound_WhenAlertDoesNotExist()
        {
            _mockAlertRepository.Setup(x => x.GetAlertAsync("12345", "alert1")).ReturnsAsync((Alert?)null);

            var request = new UpdateAlertRequest { Name = "Updated" };
            var result = await _controller.UpdateAlert("alert1", request);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateAlert_ShouldReturnOk_WhenValid()
        {
            var alert = new Alert { Id = "alert1", UserId = "12345", Name = "Original" };
            _mockAlertRepository.Setup(x => x.GetAlertAsync("12345", "alert1")).ReturnsAsync(alert);

            var request = new UpdateAlertRequest { Name = "Updated" };
            var result = await _controller.UpdateAlert("alert1", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockAlertRepository.Verify(x => x.SaveAlertAsync(It.Is<Alert>(a => a.Name == "Updated")), Times.Once);
        }

        [Fact]
        public async Task DeleteAlert_ShouldReturnBadRequest_WhenDefaultAlert()
        {
            var alert = new Alert { Id = "alert1", UserId = "12345", IsDefault = true };
            _mockAlertRepository.Setup(x => x.GetAlertAsync("12345", "alert1")).ReturnsAsync(alert);

            var result = await _controller.DeleteAlert("alert1");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task DeleteAlert_ShouldReturnOk_WhenCustomAlert()
        {
            var alert = new Alert { Id = "alert1", UserId = "12345", IsDefault = false };
            _mockAlertRepository.Setup(x => x.GetAlertAsync("12345", "alert1")).ReturnsAsync(alert);

            var result = await _controller.DeleteAlert("alert1");

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockAlertRepository.Verify(x => x.DeleteAlertAsync("12345", "alert1"), Times.Once);
        }

        [Fact]
        public async Task GetEventMappings_ShouldReturnOk()
        {
            _mockAlertRepository.Setup(x => x.GetEventMappingsAsync("12345")).ReturnsAsync(new Dictionary<string, string>());
            _mockAlertRepository.Setup(x => x.GetAlertsAsync("12345")).ReturnsAsync(new List<Alert>());

            var result = await _controller.GetEventMappings();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task ResetEventMappings_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { StreamAlerts = true } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.ResetEventMappings();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockAlertRepository.Verify(x => x.SaveEventMappingsAsync("12345", It.IsAny<Dictionary<string, string>>()), Times.Once);
        }

        [Fact]
        public async Task UpdateEventMappings_ShouldReturnOk()
        {
            var mappings = new Dictionary<string, string> { { "follow", "alert1" } };

            var result = await _controller.UpdateEventMappings(mappings);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockAlertRepository.Verify(x => x.SaveEventMappingsAsync("12345", mappings), Times.Once);
        }
    }
}
