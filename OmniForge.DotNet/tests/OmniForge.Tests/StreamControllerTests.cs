using System;
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
    public class StreamControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<IStreamMonitorService> _mockStreamMonitorService;
        private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
        private readonly StreamController _controller;

        public StreamControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockStreamMonitorService = new Mock<IStreamMonitorService>();
            _mockTwitchClientManager = new Mock<ITwitchClientManager>();

            _controller = new StreamController(
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockOverlayNotifier.Object,
                _mockStreamMonitorService.Object,
                _mockTwitchClientManager.Object);

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
        public async Task UpdateStatus_ShouldReturnOk_WhenActionIsPrep()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "offline" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateStatusRequest { Action = "prep" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "prepping")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyStreamStatusUpdateAsync("12345", "prepping"), Times.Once);
        }

        [Fact]
        public async Task UpdateStatus_ShouldReturnOk_WhenActionIsGoLive()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "prepping" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());

            var request = new UpdateStatusRequest { Action = "go-live" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "live")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyStreamStartedAsync("12345", It.IsAny<Counter>()), Times.Once);
        }

        [Fact]
        public async Task UpdateStatus_ShouldReturnOk_WhenActionIsEndStream()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "live" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());

            var request = new UpdateStatusRequest { Action = "end-stream" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "offline")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyStreamEndedAsync("12345", It.IsAny<Counter>()), Times.Once);
        }

        [Fact]
        public async Task GetSession_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345" };
            var counters = new Counter { TwitchUserId = "12345", Deaths = 5 };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(counters);

            var result = await _controller.GetSession();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task StartStream_ShouldReturnOk()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());

            var result = await _controller.StartStream();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted != null)), Times.Once);
        }

        [Fact]
        public async Task EndStream_ShouldReturnOk()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter { StreamStarted = DateTimeOffset.UtcNow });

            var result = await _controller.EndStream();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted == null)), Times.Once);
        }

        [Fact]
        public async Task UpdateSettings_ShouldReturnBadRequest_WhenThresholdsInvalid()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var settings = new StreamSettings
            {
                BitThresholds = new BitThresholds { Death = 0 } // Invalid
            };

            var result = await _controller.UpdateSettings(settings);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateSettings_ShouldReturnOk_WhenValid()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var settings = new StreamSettings
            {
                BitThresholds = new BitThresholds { Death = 100, Swear = 50, Celebration = 10 }
            };

            var result = await _controller.UpdateSettings(settings);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.IsAny<User>()), Times.Once);
        }
    }
}
