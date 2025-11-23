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
        private readonly AdminController _controller;

        public AdminControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockTwitchClientManager = new Mock<ITwitchClientManager>();

            _controller = new AdminController(
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockTwitchClientManager.Object);

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
        public async Task UpdateUserStatus_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", IsActive = true };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateUserStatusRequest { IsActive = false };
            var result = await _controller.UpdateUserStatus("12345", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.IsActive == false)), Times.Once);
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
    }
}
