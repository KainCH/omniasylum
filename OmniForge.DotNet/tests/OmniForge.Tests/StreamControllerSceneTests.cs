using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.DTOs;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class StreamControllerSceneTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<IStreamMonitorService> _mockStreamMonitorService;
        private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
        private readonly Mock<IGameContextRepository> _mockGameContextRepository;
        private readonly Mock<IGameCountersRepository> _mockGameCountersRepository;
        private readonly Mock<ILogger<StreamController>> _mockLogger;
        private readonly StreamController _controller;

        public StreamControllerSceneTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockStreamMonitorService = new Mock<IStreamMonitorService>();
            _mockTwitchClientManager = new Mock<ITwitchClientManager>();
            _mockGameContextRepository = new Mock<IGameContextRepository>();
            _mockGameCountersRepository = new Mock<IGameCountersRepository>();
            _mockLogger = new Mock<ILogger<StreamController>>();

            _controller = new StreamController(
                _mockLogger.Object,
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockOverlayNotifier.Object,
                _mockStreamMonitorService.Object,
                _mockTwitchClientManager.Object,
                _mockGameContextRepository.Object,
                _mockGameCountersRepository.Object);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        private StreamController CreateControllerWithoutUser()
        {
            var controller = new StreamController(
                _mockLogger.Object,
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockOverlayNotifier.Object,
                _mockStreamMonitorService.Object,
                _mockTwitchClientManager.Object,
                _mockGameContextRepository.Object,
                _mockGameCountersRepository.Object);

            var user = new ClaimsPrincipal(new ClaimsIdentity());
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            return controller;
        }

        [Fact]
        public async Task ReportSceneChange_ShouldReturnOk_WhenValidRequest()
        {
            var request = new SceneChangeRequest
            {
                SceneName = "Main Scene",
                PreviousScene = "Starting Soon",
                Source = "OBS"
            };

            var result = await _controller.ReportSceneChange(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(
                x => x.NotifySceneChangeAsync("12345", "Main Scene", "Starting Soon", "OBS"),
                Times.Once);
        }

        [Fact]
        public async Task ReportSceneChange_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithoutUser();
            var request = new SceneChangeRequest
            {
                SceneName = "Main Scene",
                Source = "OBS"
            };

            var result = await controller.ReportSceneChange(request);

            Assert.IsType<UnauthorizedResult>(result);
            _mockOverlayNotifier.Verify(
                x => x.NotifySceneChangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task ReportSceneChange_ShouldReturnBadRequest_WhenSceneNameEmpty()
        {
            var request = new SceneChangeRequest
            {
                SceneName = "",
                Source = "OBS"
            };

            var result = await _controller.ReportSceneChange(request);

            Assert.IsType<BadRequestObjectResult>(result);
            _mockOverlayNotifier.Verify(
                x => x.NotifySceneChangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task ReportSceneChange_ShouldReturnBadRequest_WhenSceneNameWhitespace()
        {
            var request = new SceneChangeRequest
            {
                SceneName = "   ",
                Source = "OBS"
            };

            var result = await _controller.ReportSceneChange(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReportSceneChange_ShouldDefaultSourceToOBS_WhenSourceEmpty()
        {
            var request = new SceneChangeRequest
            {
                SceneName = "Game Scene",
                Source = ""
            };

            var result = await _controller.ReportSceneChange(request);

            Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(
                x => x.NotifySceneChangeAsync("12345", "Game Scene", null, "OBS"),
                Times.Once);
        }

        [Fact]
        public async Task ReportSceneChange_ShouldPassStreamlabsSource()
        {
            var request = new SceneChangeRequest
            {
                SceneName = "Webcam Scene",
                PreviousScene = "Desktop",
                Source = "Streamlabs"
            };

            var result = await _controller.ReportSceneChange(request);

            Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(
                x => x.NotifySceneChangeAsync("12345", "Webcam Scene", "Desktop", "Streamlabs"),
                Times.Once);
        }

        [Fact]
        public async Task ReportSceneChange_ShouldAcceptNullPreviousScene()
        {
            var request = new SceneChangeRequest
            {
                SceneName = "First Scene",
                PreviousScene = null,
                Source = "OBS"
            };

            var result = await _controller.ReportSceneChange(request);

            Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(
                x => x.NotifySceneChangeAsync("12345", "First Scene", null, "OBS"),
                Times.Once);
        }
    }
}
