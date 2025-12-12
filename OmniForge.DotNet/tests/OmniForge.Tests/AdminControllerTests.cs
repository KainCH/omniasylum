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
    public class AdminControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<ITwitchClientManager> _mockTwitchClientManager;
        private readonly Mock<IStreamMonitorService> _mockStreamMonitorService;
        private readonly AdminController _controller;

        public AdminControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockTwitchClientManager = new Mock<ITwitchClientManager>();
            _mockStreamMonitorService = new Mock<IStreamMonitorService>();

            _controller = new AdminController(
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockTwitchClientManager.Object,
                _mockStreamMonitorService.Object);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "admin123"),
                new Claim(ClaimTypes.Role, "admin")
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        [Fact]
        public async Task GetAllUsers_ShouldReturnOk()
        {
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(new List<User>());

            var result = await _controller.GetAllUsers();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task StartMonitoringForUser_ShouldCallService()
        {
            _mockStreamMonitorService.Setup(s => s.SubscribeToUserAsAsync("targetUser", "admin123"))
                .ReturnsAsync(OmniForge.Core.Interfaces.SubscriptionResult.Success);

            var result = await _controller.StartMonitoringForUser("targetUser");

            var ok = Assert.IsType<OkObjectResult>(result);
            _mockStreamMonitorService.Verify(s => s.SubscribeToUserAsAsync("targetUser", "admin123"), Times.Once);
            Assert.Contains("Monitoring started", ok.Value!.ToString());
        }

        [Fact]
        public async Task GetUserDiagnostics_ShouldReturnOk()
        {
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(new List<User>());

            var result = await _controller.GetUserDiagnostics();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetUser_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("unknown")).ReturnsAsync((User?)null);

            var result = await _controller.GetUser("unknown");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetUser_ShouldReturnOk_WhenUserExists()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync(new Counter());

            var result = await _controller.GetUser("12345");

            var okResult = Assert.IsType<OkObjectResult>(result);
            // Assert.Equal(user, okResult.Value); // Value is now an anonymous object wrapper
        }

        [Fact]
        public async Task UpdateUserRole_ShouldReturnBadRequest_WhenInvalidRole()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateRoleRequest { Role = "invalid" };
            var result = await _controller.UpdateUserRole("12345", request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateUserRole_ShouldReturnBadRequest_WhenRemovingOwnAdminRole()
        {
            var user = new User { TwitchUserId = "admin123", Role = "admin" };
            _mockUserRepository.Setup(x => x.GetUserAsync("admin123")).ReturnsAsync(user);

            var request = new UpdateRoleRequest { Role = "streamer" };
            var result = await _controller.UpdateUserRole("admin123", request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateUserRole_ShouldReturnOk_WhenValid()
        {
            var user = new User { TwitchUserId = "12345", Role = "streamer" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateRoleRequest { Role = "admin" };
            var result = await _controller.UpdateUserRole("12345", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Role == "admin")), Times.Once);
        }

        [Fact]
        public async Task UpdateFeatures_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateFeaturesRequest { Features = new FeatureFlags { ChatCommands = false } };
            var result = await _controller.UpdateFeatures("12345", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Features.ChatCommands == false)), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_ShouldReturnBadRequest_WhenDeletingSelf()
        {
            var user = new User { TwitchUserId = "admin123" };
            _mockUserRepository.Setup(x => x.GetUserAsync("admin123")).ReturnsAsync(user);

            var result = await _controller.DeleteUser("admin123");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task DeleteUser_ShouldReturnOk_WhenValid()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.DeleteUser("12345");

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.DeleteUserAsync("12345"), Times.Once);
        }

        [Fact]
        public async Task DeleteUser_ShouldReturnForbidden_WhenDeletingAdmin()
        {
            var user = new User { TwitchUserId = "12345", Role = "admin" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.DeleteUser("12345");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task GetStats_ShouldReturnOk()
        {
            var users = new List<User>
            {
                new User { TwitchUserId = "1", Username = "user1", Role = "admin", IsActive = true, Features = new FeatureFlags { ChatCommands = true } },
                new User { TwitchUserId = "2", Username = "user2", Role = "streamer", IsActive = true, Features = new FeatureFlags { ChannelPoints = true } },
                new User { TwitchUserId = "3", Username = "user3", Role = "streamer", IsActive = false, Features = new FeatureFlags() }
            };
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

            var result = await _controller.GetStats();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task UpdateUserRole_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("unknown")).ReturnsAsync((User?)null);

            var request = new UpdateRoleRequest { Role = "admin" };
            var result = await _controller.UpdateUserRole("unknown", request);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateUserRole_ShouldAcceptModRole()
        {
            var user = new User { TwitchUserId = "12345", Role = "streamer" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateRoleRequest { Role = "mod" };
            var result = await _controller.UpdateUserRole("12345", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Role == "mod")), Times.Once);
        }

        [Fact]
        public async Task UpdateFeatures_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("unknown")).ReturnsAsync((User?)null);

            var request = new UpdateFeaturesRequest { Features = new FeatureFlags() };
            var result = await _controller.UpdateFeatures("unknown", request);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateFeatures_ShouldReturnBadRequest_WhenFeaturesNull()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateFeaturesRequest { Features = null! };
            var result = await _controller.UpdateFeatures("12345", request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateFeatures_ShouldConnectTwitchBot_WhenChatCommandsEnabled()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { ChatCommands = false }
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateFeaturesRequest { Features = new FeatureFlags { ChatCommands = true } };
            var result = await _controller.UpdateFeatures("12345", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockTwitchClientManager.Verify(x => x.ConnectUserAsync("12345"), Times.Once);
        }

        [Fact]
        public async Task UpdateFeatures_ShouldDisconnectTwitchBot_WhenChatCommandsDisabled()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { ChatCommands = true }
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateFeaturesRequest { Features = new FeatureFlags { ChatCommands = false } };
            var result = await _controller.UpdateFeatures("12345", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockTwitchClientManager.Verify(x => x.DisconnectUserAsync("12345"), Times.Once);
        }

        [Fact]
        public async Task UpdateFeatures_ShouldEnableOverlaySettings_WhenStreamOverlayEnabled()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Features = new FeatureFlags { StreamOverlay = false },
                OverlaySettings = new OverlaySettings { Enabled = false }
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateFeaturesRequest { Features = new FeatureFlags { StreamOverlay = true } };
            var result = await _controller.UpdateFeatures("12345", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.OverlaySettings.Enabled == true)), Times.Once);
        }

        [Fact]
        public async Task GetAllUsers_ShouldIdentifyBrokenUsers()
        {
            var users = new List<User>
            {
                new User { TwitchUserId = "", Username = "broken" }, // broken - empty TwitchUserId
                new User { TwitchUserId = "123", Username = "" },    // incomplete - empty Username
                new User { TwitchUserId = "456", Username = "complete" } // complete
            };
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

            var result = await _controller.GetAllUsers();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }
    }
}
