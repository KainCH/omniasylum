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
        private readonly Mock<IGameContextRepository> _mockGameContextRepository;
        private readonly Mock<IGameCountersRepository> _mockGameCountersRepository;
        private readonly StreamController _controller;

        public StreamControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockStreamMonitorService = new Mock<IStreamMonitorService>();
            _mockTwitchClientManager = new Mock<ITwitchClientManager>();
            _mockGameContextRepository = new Mock<IGameContextRepository>();
            _mockGameCountersRepository = new Mock<IGameCountersRepository>();

            _controller = new StreamController(
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

        #region UpdateStatus Tests

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
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter
            {
                TwitchUserId = "12345",
                CustomCounters = new System.Collections.Generic.Dictionary<string, int> { ["kills"] = 7 }
            });
            _mockGameContextRepository.Setup(x => x.GetAsync("12345")).ReturnsAsync(new GameContext
            {
                UserId = "12345",
                ActiveGameId = "game-abc",
                ActiveGameName = "Test Category"
            });
            _mockGameCountersRepository.Setup(x => x.SaveAsync("12345", "game-abc", It.IsAny<Counter>())).Returns(Task.CompletedTask);

            var request = new UpdateStatusRequest { Action = "end-stream" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "offline")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyStreamEndedAsync("12345", It.IsAny<Counter>()), Times.Once);

            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c =>
                c.LastCategoryName == "Test Category" &&
                c.CustomCounters.ContainsKey("kills") &&
                c.CustomCounters["kills"] == 7)), Times.Once);

            _mockGameCountersRepository.Verify(x => x.SaveAsync("12345", "game-abc", It.Is<Counter>(c =>
                c.LastCategoryName == "Test Category" &&
                c.CustomCounters.ContainsKey("kills") &&
                c.CustomCounters["kills"] == 7)), Times.Once);
        }

        [Fact]
        public async Task UpdateStatus_ShouldReturnOk_WhenActionIsCancelPrep()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "prepping" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateStatusRequest { Action = "cancel-prep" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "offline")), Times.Once);
        }

        [Fact]
        public async Task UpdateStatus_ShouldReturnBadRequest_WhenActionIsInvalid()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "offline" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateStatusRequest { Action = "invalid-action" };
            var result = await _controller.UpdateStatus(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateStatus_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var request = new UpdateStatusRequest { Action = "prep" };
            var result = await controller.UpdateStatus(request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateStatus_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var request = new UpdateStatusRequest { Action = "prep" };
            var result = await _controller.UpdateStatus(request);

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task UpdateStatus_PrepShouldNotTransition_WhenAlreadyLive()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "live" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateStatusRequest { Action = "prep" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "live")), Times.Once);
        }

        [Fact]
        public async Task UpdateStatus_GoLiveShouldNotTransition_WhenOffline()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "offline" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateStatusRequest { Action = "go-live" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "offline")), Times.Once);
        }

        [Fact]
        public async Task UpdateStatus_EndStreamFromEnding_ShouldTransitionToOffline()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "ending" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());

            var request = new UpdateStatusRequest { Action = "end-stream" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "offline")), Times.Once);
        }

        [Fact]
        public async Task UpdateStatus_ShouldHandleNullStreamStatus()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = null! };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateStatusRequest { Action = "prep" };
            var result = await _controller.UpdateStatus(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region GetSession Tests

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
        public async Task GetSession_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.GetSession();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetSession_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.GetSession();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task GetSession_ShouldIndicateLive_WhenStreamStartedNotNull()
        {
            var user = new User { TwitchUserId = "12345" };
            var counters = new Counter { TwitchUserId = "12345", StreamStarted = DateTimeOffset.UtcNow };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(counters);

            var result = await _controller.GetSession();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        #endregion

        #region StartStream Tests

        [Fact]
        public async Task StartStream_ShouldReturnOk()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());

            var result = await _controller.StartStream();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted != null)), Times.Once);
        }

        [Fact]
        public async Task StartStream_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.StartStream();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task StartStream_ShouldCreateNewCounters_WhenNull()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync((Counter?)null);

            var result = await _controller.StartStream();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.TwitchUserId == "12345")), Times.Once);
        }

        [Fact]
        public async Task StartStream_ShouldResetBits()
        {
            var counter = new Counter { Bits = 100 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(counter);

            var result = await _controller.StartStream();

            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.Bits == 0)), Times.Once);
        }

        [Fact]
        public async Task StartStream_WhenSavedCountersExistForActiveGame_ShouldLoadCounterStateForGame()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter { TwitchUserId = "12345", Deaths = 1 });
            _mockGameContextRepository.Setup(x => x.GetAsync("12345")).ReturnsAsync(new GameContext
            {
                UserId = "12345",
                ActiveGameId = "game-abc",
                ActiveGameName = "Test Category"
            });

            _mockGameCountersRepository.Setup(x => x.GetAsync("12345", "game-abc")).ReturnsAsync(new Counter
            {
                TwitchUserId = "12345",
                Deaths = 42,
                Swears = 3,
                CustomCounters = new System.Collections.Generic.Dictionary<string, int> { ["kills"] = 7 }
            });

            var result = await _controller.StartStream();

            Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c =>
                c.Deaths == 42 &&
                c.Swears == 3 &&
                c.CustomCounters.ContainsKey("kills") &&
                c.CustomCounters["kills"] == 7 &&
                c.LastCategoryName == "Test Category" &&
                c.Bits == 0 &&
                c.StreamStarted != null
            )), Times.Once);
        }

        #endregion

        #region EndStream Tests

        [Fact]
        public async Task EndStream_ShouldReturnOk()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter { StreamStarted = DateTimeOffset.UtcNow });

            var result = await _controller.EndStream();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.StreamStarted == null)), Times.Once);
        }

        [Fact]
        public async Task EndStream_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.EndStream();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task EndStream_ShouldCreateNewCounters_WhenNull()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync((Counter?)null);

            var result = await _controller.EndStream();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.TwitchUserId == "12345")), Times.Once);
        }

        #endregion

        #region GetSettings Tests

        [Fact]
        public async Task GetSettings_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.GetSettings();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetSettings_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.GetSettings();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetSettings_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.GetSettings();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        #endregion

        #region UpdateSettings Tests

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
        public async Task UpdateSettings_ShouldReturnBadRequest_WhenSwearThresholdInvalid()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var settings = new StreamSettings
            {
                BitThresholds = new BitThresholds { Death = 100, Swear = 0, Celebration = 10 }
            };

            var result = await _controller.UpdateSettings(settings);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateSettings_ShouldReturnBadRequest_WhenCelebrationThresholdInvalid()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var settings = new StreamSettings
            {
                BitThresholds = new BitThresholds { Death = 100, Swear = 50, Celebration = 0 }
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

        [Fact]
        public async Task UpdateSettings_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var settings = new StreamSettings();
            var result = await controller.UpdateSettings(settings);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateSettings_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var settings = new StreamSettings();
            var result = await _controller.UpdateSettings(settings);

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task UpdateSettings_ShouldReturnOk_WhenThresholdsNull()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var settings = new StreamSettings { BitThresholds = null! };

            var result = await _controller.UpdateSettings(settings);

            var okResult = Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region ResetBits Tests

        [Fact]
        public async Task ResetBits_ShouldReturnOk()
        {
            var counter = new Counter { Bits = 100 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(counter);

            var result = await _controller.ResetBits();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(0, counter.Bits);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counter), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", counter), Times.Once);
        }

        [Fact]
        public async Task ResetBits_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.ResetBits();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task ResetBits_ShouldReturnNotFound_WhenCountersNotFound()
        {
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync((Counter?)null);

            var result = await _controller.ResetBits();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("Counters not found", notFoundResult.Value);
        }

        #endregion

        #region GetMonitorStatus Tests

        [Fact]
        public void GetMonitorStatus_ShouldReturnOk()
        {
            var status = new StreamMonitorStatus { Connected = true };
            var botStatus = new BotStatus { Connected = true };

            _mockStreamMonitorService.Setup(x => x.GetUserConnectionStatus("12345")).Returns(status);
            _mockStreamMonitorService.Setup(x => x.IsUserSubscribed("12345")).Returns(true);
            _mockTwitchClientManager.Setup(x => x.GetUserBotStatus("12345")).Returns(botStatus);

            var result = _controller.GetMonitorStatus();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public void GetMonitorStatus_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = controller.GetMonitorStatus();

            Assert.IsType<UnauthorizedResult>(result);
        }

        #endregion

        #region SubscribeMonitor Tests

        [Fact]
        public async Task SubscribeMonitor_ShouldReturnOk_WhenSuccess()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { ChatCommands = true }, AccessToken = "token" };
            _mockStreamMonitorService.Setup(x => x.SubscribeToUserAsync("12345")).ReturnsAsync(SubscriptionResult.Success);
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.SubscribeMonitor();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SubscribeMonitor_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.SubscribeMonitor();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task SubscribeMonitor_ShouldReturnForbidden_WhenRequiresReauth()
        {
            _mockStreamMonitorService.Setup(x => x.SubscribeToUserAsync("12345")).ReturnsAsync(SubscriptionResult.RequiresReauth);

            var result = await _controller.SubscribeMonitor();

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task SubscribeMonitor_ShouldReturnUnauthorized_WhenUnauthorized()
        {
            _mockStreamMonitorService.Setup(x => x.SubscribeToUserAsync("12345")).ReturnsAsync(SubscriptionResult.Unauthorized);

            var result = await _controller.SubscribeMonitor();

            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task SubscribeMonitor_ShouldReturnBadRequest_WhenFailed()
        {
            _mockStreamMonitorService.Setup(x => x.SubscribeToUserAsync("12345")).ReturnsAsync(SubscriptionResult.Failed);

            var result = await _controller.SubscribeMonitor();

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SubscribeMonitor_ShouldNotConnectBot_WhenChatCommandsDisabled()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { ChatCommands = false }, AccessToken = "token" };
            _mockStreamMonitorService.Setup(x => x.SubscribeToUserAsync("12345")).ReturnsAsync(SubscriptionResult.Success);
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.SubscribeMonitor();

            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("12345"), Times.Never);
        }

        [Fact]
        public async Task SubscribeMonitor_ShouldNotConnectBot_WhenNoAccessToken()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { ChatCommands = true }, AccessToken = "" };
            _mockStreamMonitorService.Setup(x => x.SubscribeToUserAsync("12345")).ReturnsAsync(SubscriptionResult.Success);
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.SubscribeMonitor();

            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("12345"), Times.Never);
        }

        #endregion

        #region UnsubscribeMonitor Tests

        [Fact]
        public async Task UnsubscribeMonitor_ShouldReturnOk()
        {
            var result = await _controller.UnsubscribeMonitor();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockStreamMonitorService.Verify(x => x.UnsubscribeFromUserAsync("12345"), Times.Once);
            _mockTwitchClientManager.Verify(x => x.DisconnectUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task UnsubscribeMonitor_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.UnsubscribeMonitor();

            Assert.IsType<UnauthorizedResult>(result);
        }

        #endregion

        #region ReconnectMonitor Tests

        [Fact]
        public async Task ReconnectMonitor_ShouldReturnOk_WhenSuccess()
        {
            _mockStreamMonitorService.Setup(x => x.ForceReconnectUserAsync("12345")).ReturnsAsync(SubscriptionResult.Success);

            var result = await _controller.ReconnectMonitor();

            var okResult = Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task ReconnectMonitor_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.ReconnectMonitor();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task ReconnectMonitor_ShouldReturnForbidden_WhenRequiresReauth()
        {
            _mockStreamMonitorService.Setup(x => x.ForceReconnectUserAsync("12345")).ReturnsAsync(SubscriptionResult.RequiresReauth);

            var result = await _controller.ReconnectMonitor();

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task ReconnectMonitor_ShouldReturnServerError_WhenFailed()
        {
            _mockStreamMonitorService.Setup(x => x.ForceReconnectUserAsync("12345")).ReturnsAsync(SubscriptionResult.Failed);

            var result = await _controller.ReconnectMonitor();

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }

        #endregion

        #region GetEventSubStatus Tests

        [Fact]
        public async Task GetEventSubStatus_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", Username = "testuser", DiscordWebhookUrl = "http://webhook.com" };
            var status = new StreamMonitorStatus { Connected = true };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockStreamMonitorService.Setup(x => x.GetUserConnectionStatus("12345")).Returns(status);

            var result = await _controller.GetEventSubStatus();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetEventSubStatus_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.GetEventSubStatus();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetEventSubStatus_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.GetEventSubStatus();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        #endregion

        #region GetBotStatus Tests

        [Fact]
        public async Task GetBotStatus_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", Username = "testuser", AccessToken = "token", Features = new FeatureFlags { ChatCommands = true } };
            var botStatus = new BotStatus { Connected = true };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockTwitchClientManager.Setup(x => x.GetUserBotStatus("12345")).Returns(botStatus);

            var result = await _controller.GetBotStatus();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetBotStatus_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.GetBotStatus();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetBotStatus_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.GetBotStatus();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        #endregion

        #region ToggleBot Tests

        [Fact]
        public async Task ToggleBot_ShouldStartBot_WhenActionIsStart()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { ChatCommands = true }, AccessToken = "token" };
            var botStatus = new BotStatus { Connected = true };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockTwitchClientManager.Setup(x => x.GetUserBotStatus("12345")).Returns(botStatus);

            var request = new ToggleBotRequest { Action = "start" };
            var result = await _controller.ToggleBot(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("12345"), Times.Once);
        }

        [Fact]
        public async Task ToggleBot_ShouldStopBot_WhenActionIsStop()
        {
            var user = new User { TwitchUserId = "12345" };
            var botStatus = new BotStatus { Connected = false };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockTwitchClientManager.Setup(x => x.GetUserBotStatus("12345")).Returns(botStatus);

            var request = new ToggleBotRequest { Action = "stop" };
            var result = await _controller.ToggleBot(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockTwitchClientManager.Verify(x => x.DisconnectUserAsync("12345"), Times.Once);
        }

        [Fact]
        public async Task ToggleBot_ShouldReturnBadRequest_WhenInvalidAction()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new ToggleBotRequest { Action = "invalid" };
            var result = await _controller.ToggleBot(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ToggleBot_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var request = new ToggleBotRequest { Action = "start" };
            var result = await controller.ToggleBot(request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task ToggleBot_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var request = new ToggleBotRequest { Action = "start" };
            var result = await _controller.ToggleBot(request);

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        [Fact]
        public async Task ToggleBot_ShouldReturnBadRequest_WhenChatCommandsDisabled()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { ChatCommands = false } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new ToggleBotRequest { Action = "start" };
            var result = await _controller.ToggleBot(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ToggleBot_ShouldReturnBadRequest_WhenNoAccessToken()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { ChatCommands = true }, AccessToken = "" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new ToggleBotRequest { Action = "start" };
            var result = await _controller.ToggleBot(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        #region GetStatus Tests

        [Fact]
        public async Task GetStatus_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", Username = "testuser", DisplayName = "Test User" };
            var counters = new Counter { Deaths = 5 };

            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(counters);

            var result = await _controller.GetStatus();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetStatus_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controller = CreateControllerWithoutUser();

            var result = await controller.GetStatus();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetStatus_ShouldReturnNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.GetStatus();

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("User not found", notFoundResult.Value);
        }

        #endregion

        #region Phase 1 Endpoint Tests

        [Fact]
        public async Task PrepStream_ShouldCallUpdateStatus()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "offline" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.PrepStream();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "prepping")), Times.Once);
        }

        [Fact]
        public async Task GoLive_ShouldCallUpdateStatus()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "prepping" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());

            var result = await _controller.GoLive();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "live")), Times.Once);
        }

        [Fact]
        public async Task EndStreamPhase1_ShouldCallUpdateStatus()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "live" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());

            var result = await _controller.EndStreamPhase1();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "offline")), Times.Once);
        }

        [Fact]
        public async Task CancelPrep_ShouldCallUpdateStatus()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "prepping" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.CancelPrep();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.StreamStatus == "offline")), Times.Once);
        }

        #endregion
    }
}
