using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests.Controllers
{
    public class OverlaySseControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository = new();
        private readonly Mock<ICounterRepository> _mockCounterRepository = new();
        private readonly Mock<IAlertRepository> _mockAlertRepository = new();
        private readonly Mock<IStreamMonitorService> _mockStreamMonitor = new();
        private readonly Mock<ILogger<OverlaySseController>> _mockLogger = new();
        private readonly SseConnectionManager _sseManager;

        public OverlaySseControllerTests()
        {
            _sseManager = new SseConnectionManager(new Mock<ILogger<SseConnectionManager>>().Object);
        }

        private OverlaySseController CreateController()
        {
            var controller = new OverlaySseController(
                _sseManager,
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockAlertRepository.Object,
                _mockStreamMonitor.Object,
                _mockLogger.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        [Fact]
        public async Task SignalReady_WhenUserIdEmpty_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.SignalReady("", "conn1");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SignalReady_WhenConnectionIdEmpty_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.SignalReady("user1", "");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SignalReady_WhenUserNotFound_Returns403()
        {
            var controller = CreateController();
            _mockUserRepository.Setup(x => x.GetUserAsync("user1")).ReturnsAsync((User?)null);

            var result = await controller.SignalReady("user1", "conn1");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task SignalReady_WhenStreamOverlayDisabled_Returns403()
        {
            var controller = CreateController();
            _mockUserRepository.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "user1",
                    Features = new FeatureFlags { StreamOverlay = false }
                });

            var result = await controller.SignalReady("user1", "conn1");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task SignalReady_WhenStreamOverlayEnabled_ConnectionNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _mockUserRepository.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "user1",
                    Features = new FeatureFlags { StreamOverlay = true }
                });
            _mockCounterRepository.Setup(x => x.GetCountersAsync("user1"))
                .ReturnsAsync(new Counter { TwitchUserId = "user1" });
            _mockAlertRepository.Setup(x => x.GetAlertsAsync("user1"))
                .ReturnsAsync(new List<Alert>());
            _mockCounterRepository.Setup(x => x.GetCustomCountersConfigAsync("user1"))!
                .ReturnsAsync((CustomCounterConfiguration?)null);

            var result = await controller.SignalReady("user1", "nonexistent");

            Assert.IsType<NotFoundObjectResult>(result);
        }
    }
}
