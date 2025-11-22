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
    public class ModeratorControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly ModeratorController _controller;

        public ModeratorControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();

            _controller = new ModeratorController(
                _mockUserRepository.Object,
                _mockCounterRepository.Object);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "streamer123"),
                new Claim("username", "streamer")
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        [Fact]
        public async Task GetMyModerators_ShouldReturnOk()
        {
            var users = new List<User>
            {
                new User { TwitchUserId = "mod1", Role = "mod", ManagedStreamers = new List<string> { "streamer123" } }
            };
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

            var result = await _controller.GetMyModerators();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GrantAccess_ShouldReturnBadRequest_WhenMissingId()
        {
            var request = new GrantAccessRequest { ModeratorUserId = "" };
            var result = await _controller.GrantAccess(request);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GrantAccess_ShouldReturnBadRequest_WhenSelf()
        {
            var request = new GrantAccessRequest { ModeratorUserId = "streamer123" };
            var result = await _controller.GrantAccess(request);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GrantAccess_ShouldReturnNotFound_WhenModNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("unknown")).ReturnsAsync((User?)null);
            var request = new GrantAccessRequest { ModeratorUserId = "unknown" };
            var result = await _controller.GrantAccess(request);
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GrantAccess_ShouldReturnBadRequest_WhenAdmin()
        {
            var admin = new User { TwitchUserId = "admin", Role = "admin" };
            _mockUserRepository.Setup(x => x.GetUserAsync("admin")).ReturnsAsync(admin);
            var request = new GrantAccessRequest { ModeratorUserId = "admin" };
            var result = await _controller.GrantAccess(request);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GrantAccess_ShouldReturnConflict_WhenAlreadyMod()
        {
            var mod = new User { TwitchUserId = "mod1", Role = "mod", ManagedStreamers = new List<string> { "streamer123" } };
            _mockUserRepository.Setup(x => x.GetUserAsync("mod1")).ReturnsAsync(mod);
            var request = new GrantAccessRequest { ModeratorUserId = "mod1" };
            var result = await _controller.GrantAccess(request);
            Assert.IsType<ConflictObjectResult>(result);
        }

        [Fact]
        public async Task GrantAccess_ShouldReturnOk_WhenValid()
        {
            var user = new User { TwitchUserId = "user1", Role = "streamer" };
            _mockUserRepository.Setup(x => x.GetUserAsync("user1")).ReturnsAsync(user);
            var request = new GrantAccessRequest { ModeratorUserId = "user1" };
            var result = await _controller.GrantAccess(request);
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Role == "mod" && u.ManagedStreamers.Contains("streamer123"))), Times.Once);
        }

        [Fact]
        public async Task RevokeAccess_ShouldReturnNotFound_WhenModNotFound()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("unknown")).ReturnsAsync((User?)null);
            var result = await _controller.RevokeAccess("unknown");
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task RevokeAccess_ShouldReturnNotFound_WhenNotMod()
        {
            var user = new User { TwitchUserId = "user1", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(x => x.GetUserAsync("user1")).ReturnsAsync(user);
            var result = await _controller.RevokeAccess("user1");
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task RevokeAccess_ShouldReturnOk_WhenValid()
        {
            var mod = new User { TwitchUserId = "mod1", Role = "mod", ManagedStreamers = new List<string> { "streamer123" } };
            _mockUserRepository.Setup(x => x.GetUserAsync("mod1")).ReturnsAsync(mod);
            var result = await _controller.RevokeAccess("mod1");
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => !u.ManagedStreamers.Contains("streamer123"))), Times.Once);
        }

        [Fact]
        public async Task SearchUsers_ShouldReturnBadRequest_WhenQueryTooShort()
        {
            var result = await _controller.SearchUsers("a");
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SearchUsers_ShouldReturnOk()
        {
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(new List<User>());
            var result = await _controller.SearchUsers("test");
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetManagedStreamers_ShouldReturnOk()
        {
            var mod = new User { TwitchUserId = "streamer123", ManagedStreamers = new List<string> { "streamer2" } };
            _mockUserRepository.Setup(x => x.GetUserAsync("streamer123")).ReturnsAsync(mod);
            _mockUserRepository.Setup(x => x.GetUserAsync("streamer2")).ReturnsAsync(new User { TwitchUserId = "streamer2" });
            _mockCounterRepository.Setup(x => x.GetCountersAsync("streamer2")).ReturnsAsync(new Counter());

            var result = await _controller.GetManagedStreamers();
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetStreamerDetails_ShouldReturnForbid_WhenNotAuthorized()
        {
            var mod = new User { TwitchUserId = "streamer123", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(x => x.GetUserAsync("streamer123")).ReturnsAsync(mod);

            var result = await _controller.GetStreamerDetails("streamer2");
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetStreamerDetails_ShouldReturnOk_WhenAuthorized()
        {
            var mod = new User { TwitchUserId = "streamer123", ManagedStreamers = new List<string> { "streamer2" } };
            _mockUserRepository.Setup(x => x.GetUserAsync("streamer123")).ReturnsAsync(mod);
            _mockUserRepository.Setup(x => x.GetUserAsync("streamer2")).ReturnsAsync(new User { TwitchUserId = "streamer2" });
            _mockCounterRepository.Setup(x => x.GetCountersAsync("streamer2")).ReturnsAsync(new Counter());

            var result = await _controller.GetStreamerDetails("streamer2");
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }
    }
}
