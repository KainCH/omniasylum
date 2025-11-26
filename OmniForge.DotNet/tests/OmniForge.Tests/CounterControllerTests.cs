using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class CounterControllerTests
    {
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<INotificationService> _mockNotificationService;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ILogger<CounterController>> _mockLogger;
        private readonly CounterController _controller;

        public CounterControllerTests()
        {
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockNotificationService = new Mock<INotificationService>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockLogger = new Mock<ILogger<CounterController>>();

            _controller = new CounterController(
                _mockCounterRepository.Object,
                _mockUserRepository.Object,
                _mockNotificationService.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object);

            // Setup User Context
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
        public async Task Increment_ShouldReturnOk_WhenValid()
        {
            var counter = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.IncrementCounterAsync("12345", "deaths", 1))
                .ReturnsAsync(counter);

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { TwitchUserId = "12345" });

            var result = await _controller.Increment("deaths");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(counter, okResult.Value);

            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", counter), Times.Once);
            _mockNotificationService.Verify(x => x.CheckAndSendMilestoneNotificationsAsync(It.IsAny<User>(), "deaths", 9, 10), Times.Once);
        }

        [Fact]
        public async Task Increment_ShouldReturnBadRequest_WhenInvalidType()
        {
            _mockCounterRepository.Setup(x => x.IncrementCounterAsync("12345", "invalid", 1))
                .ThrowsAsync(new System.ArgumentException());

            var result = await _controller.Increment("invalid");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Decrement_ShouldReturnOk_WhenValid()
        {
            var counter = new Counter { Deaths = 9 };
            _mockCounterRepository.Setup(x => x.DecrementCounterAsync("12345", "deaths", 1))
                .ReturnsAsync(counter);

            var result = await _controller.Decrement("deaths");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(counter, okResult.Value);

            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", counter), Times.Once);
        }

        [Fact]
        public async Task Decrement_ShouldReturnBadRequest_WhenInvalidType()
        {
            _mockCounterRepository.Setup(x => x.DecrementCounterAsync("12345", "invalid", 1))
                .ThrowsAsync(new System.ArgumentException());

            var result = await _controller.Decrement("invalid");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetCounters_ShouldReturnOk_WhenAuthorized()
        {
            var counter = new Counter { Deaths = 5, Swears = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            var result = await _controller.GetCounters();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(counter, okResult.Value);
        }
    }
}
