using System;
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

        private CounterController CreateControllerWithoutUser()
        {
            var controller = new CounterController(
                _mockCounterRepository.Object,
                _mockUserRepository.Object,
                _mockNotificationService.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object);

            // No userId claim
            var user = new ClaimsPrincipal(new ClaimsIdentity());
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            return controller;
        }

        #region GetCounters Tests

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

        [Fact]
        public async Task GetCounters_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.GetCounters();

            Assert.IsType<UnauthorizedResult>(result);
        }

        #endregion

        #region Increment Tests

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
        public async Task Increment_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.Increment("deaths");

            Assert.IsType<UnauthorizedResult>(result);
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
        public async Task Increment_ShouldNotSendMilestoneNotifications_WhenUserIsNull()
        {
            var counter = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.IncrementCounterAsync("12345", "deaths", 1))
                .ReturnsAsync(counter);

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync((User?)null);

            var result = await _controller.Increment("deaths");

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockNotificationService.Verify(x => x.CheckAndSendMilestoneNotificationsAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Theory]
        [InlineData("swears", 5)]
        [InlineData("screams", 3)]
        [InlineData("bits", 100)]
        public async Task Increment_ShouldWorkForAllCounterTypes(string type, int expectedValue)
        {
            var counter = new Counter();
            switch (type)
            {
                case "swears": counter.Swears = expectedValue; break;
                case "screams": counter.Screams = expectedValue; break;
                case "bits": counter.Bits = expectedValue; break;
            }

            _mockCounterRepository.Setup(x => x.IncrementCounterAsync("12345", type, 1))
                .ReturnsAsync(counter);

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User { TwitchUserId = "12345" });

            var result = await _controller.Increment(type);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        #endregion

        #region Decrement Tests

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
        public async Task Decrement_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.Decrement("deaths");

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Decrement_ShouldReturnBadRequest_WhenInvalidType()
        {
            _mockCounterRepository.Setup(x => x.DecrementCounterAsync("12345", "invalid", 1))
                .ThrowsAsync(new System.ArgumentException());

            var result = await _controller.Decrement("invalid");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Theory]
        [InlineData("swears")]
        [InlineData("screams")]
        [InlineData("bits")]
        public async Task Decrement_ShouldWorkForAllCounterTypes(string type)
        {
            var counter = new Counter();
            _mockCounterRepository.Setup(x => x.DecrementCounterAsync("12345", type, 1))
                .ReturnsAsync(counter);

            var result = await _controller.Decrement(type);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        #endregion

        #region ResetCounters Tests

        [Fact]
        public async Task ResetCounters_ShouldReturnOk_WhenValid()
        {
            var counter = new Counter { Deaths = 10, Swears = 5, Screams = 3, Bits = 100 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            var result = await _controller.ResetCounters();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedCounter = okResult.Value as Counter;
            Assert.NotNull(returnedCounter);
            Assert.Equal(0, returnedCounter.Deaths);
            Assert.Equal(0, returnedCounter.Swears);
            Assert.Equal(0, returnedCounter.Screams);
            Assert.Equal(100, returnedCounter.Bits); // Bits preserved

            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.IsAny<Counter>()), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", It.IsAny<Counter>()), Times.Once);
        }

        [Fact]
        public async Task ResetCounters_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.ResetCounters();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task ResetCounters_ShouldReturnNotFound_WhenCountersNotExist()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync((Counter?)null);

            var result = await _controller.ResetCounters();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("Counters not found", notFoundResult.Value);
        }

        #endregion

        #region ExportData Tests

        [Fact]
        public async Task ExportData_ShouldReturnOk_WhenValid()
        {
            var counter = new Counter
            {
                Deaths = 10,
                Swears = 5,
                Screams = 3,
                Bits = 100,
                LastUpdated = DateTimeOffset.UtcNow
            };
            var user = new User { TwitchUserId = "12345", Username = "testuser" };

            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var result = await _controller.ExportData();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task ExportData_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.ExportData();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task ExportData_ShouldReturnNotFound_WhenCountersNotExist()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync((Counter?)null);

            var result = await _controller.ExportData();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("Counters not found", notFoundResult.Value);
        }

        [Fact]
        public async Task ExportData_ShouldIncludeNullUsername_WhenUserNotFound()
        {
            var counter = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync((User?)null);

            var result = await _controller.ExportData();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        #endregion

        #region UpdateBitsProgress Tests

        [Fact]
        public async Task UpdateBitsProgress_ShouldReturnOk_WhenValid()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                OverlaySettings = new OverlaySettings
                {
                    BitsGoal = new BitsGoal { Target = 1000, Current = 100 }
                }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new BitsProgressRequest { Amount = 50 };
            var result = await _controller.UpdateBitsProgress(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            _mockUserRepository.Verify(x => x.SaveUserAsync(It.IsAny<User>()), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifySettingsUpdateAsync("12345", It.IsAny<OverlaySettings>()), Times.Once);
        }

        [Fact]
        public async Task UpdateBitsProgress_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var request = new BitsProgressRequest { Amount = 50 };
            var result = await controller.UpdateBitsProgress(request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateBitsProgress_ShouldReturnBadRequest_WhenAmountIsZeroOrNegative()
        {
            var request = new BitsProgressRequest { Amount = 0 };
            var result = await _controller.UpdateBitsProgress(request);

            Assert.IsType<BadRequestObjectResult>(result);

            request = new BitsProgressRequest { Amount = -5 };
            result = await _controller.UpdateBitsProgress(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateBitsProgress_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync((User?)null);

            var request = new BitsProgressRequest { Amount = 50 };
            var result = await _controller.UpdateBitsProgress(request);

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task UpdateBitsProgress_ShouldReturnBadRequest_WhenBitsGoalNotConfigured()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                OverlaySettings = new OverlaySettings { BitsGoal = null! }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new BitsProgressRequest { Amount = 50 };
            var result = await _controller.UpdateBitsProgress(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateBitsProgress_ShouldCapAtTarget()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                OverlaySettings = new OverlaySettings
                {
                    BitsGoal = new BitsGoal { Target = 100, Current = 90 }
                }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new BitsProgressRequest { Amount = 50 }; // Would exceed target
            var result = await _controller.UpdateBitsProgress(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(100, user.OverlaySettings.BitsGoal.Current); // Capped at target
        }

        [Fact]
        public async Task UpdateBitsProgress_ShouldIndicateGoalReached()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                OverlaySettings = new OverlaySettings
                {
                    BitsGoal = new BitsGoal { Target = 100, Current = 50 }
                }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new BitsProgressRequest { Amount = 50 };
            var result = await _controller.UpdateBitsProgress(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        #endregion

        #region GetOverlaySettings Tests

        [Fact]
        public async Task GetOverlaySettings_ShouldReturnOk_WhenFeatureEnabled()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = true },
                OverlaySettings = new OverlaySettings { Position = "top-right" }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var result = await _controller.GetOverlaySettings();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetOverlaySettings_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.GetOverlaySettings();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetOverlaySettings_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync((User?)null);

            var result = await _controller.GetOverlaySettings();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task GetOverlaySettings_ShouldReturnForbidden_WhenFeatureNotEnabled()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = false }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var result = await _controller.GetOverlaySettings();

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task GetOverlaySettings_ShouldReturnDefaultSettings_WhenNull()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = true },
                OverlaySettings = null!
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var result = await _controller.GetOverlaySettings();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        #endregion

        #region UpdateOverlaySettings Tests

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnOk_WhenValid()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = true }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new OverlaySettings { Position = "top-left", Scale = 1.5 };
            var result = await _controller.UpdateOverlaySettings(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var request = new OverlaySettings { Position = "top-left" };
            var result = await controller.UpdateOverlaySettings(request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnBadRequest_WhenRequestIsNull()
        {
            var result = await _controller.UpdateOverlaySettings(null!);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequestResult.Value);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync((User?)null);

            var request = new OverlaySettings { Position = "top-left" };
            var result = await _controller.UpdateOverlaySettings(request);

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnForbidden_WhenFeatureNotEnabled()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = false }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new OverlaySettings { Position = "top-left" };
            var result = await _controller.UpdateOverlaySettings(request);

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnBadRequest_WhenInvalidPosition()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = true }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new OverlaySettings { Position = "invalid-position" };
            var result = await _controller.UpdateOverlaySettings(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Theory]
        [InlineData("top-left")]
        [InlineData("top-right")]
        [InlineData("bottom-left")]
        [InlineData("bottom-right")]
        public async Task UpdateOverlaySettings_ShouldAcceptValidPositions(string position)
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = true }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new OverlaySettings { Position = position };
            var result = await _controller.UpdateOverlaySettings(request);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldAcceptEmptyPosition()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = true }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new OverlaySettings { Position = "" };
            var result = await _controller.UpdateOverlaySettings(request);

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region GetPublicCounters Tests

        [Fact]
        public async Task GetPublicCounters_ShouldReturnOk_WhenValid()
        {
            var counter = new Counter
            {
                Deaths = 10,
                Swears = 5,
                Screams = 3,
                Bits = 100,
                StreamStarted = DateTimeOffset.UtcNow
            };
            var user = new User
            {
                TwitchUserId = "user123",
                OverlaySettings = new OverlaySettings { Position = "top-right" }
            };

            _mockCounterRepository.Setup(x => x.GetCountersAsync("user123"))
                .ReturnsAsync(counter);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123"))
                .ReturnsAsync(user);

            var result = await _controller.GetPublicCounters("user123");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetPublicCounters_ShouldReturnNotFound_WhenCountersNotExist()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("user123"))
                .ReturnsAsync((Counter?)null);

            var result = await _controller.GetPublicCounters("user123");

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task GetPublicCounters_ShouldReturnNotFound_WhenUserNotFound()
        {
            var counter = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("user123"))
                .ReturnsAsync(counter);
            _mockUserRepository.Setup(x => x.GetUserAsync("user123"))
                .ReturnsAsync((User?)null);

            var result = await _controller.GetPublicCounters("user123");

            Assert.IsType<NotFoundResult>(result);
        }

        #endregion
    }
}
