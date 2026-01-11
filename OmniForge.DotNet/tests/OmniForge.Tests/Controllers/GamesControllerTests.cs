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

namespace OmniForge.Tests.Controllers
{
    public class GamesControllerTests
    {
        private readonly Mock<ITwitchApiService> _mockTwitchApiService = new();
        private readonly Mock<IGameLibraryRepository> _mockGameLibraryRepository = new();
        private readonly Mock<IGameContextRepository> _mockGameContextRepository = new();
        private readonly Mock<ILogger<GamesController>> _mockLogger = new();

        private GamesController CreateControllerWithUser(string? userId)
        {
            var controller = new GamesController(
                _mockTwitchApiService.Object,
                _mockGameLibraryRepository.Object,
                _mockGameContextRepository.Object,
                _mockLogger.Object);

            var httpContext = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(userId))
            {
                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("userId", userId)
                }, "test"));
            }

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task Search_WhenNoUserId_ShouldReturnUnauthorized()
        {
            var controller = CreateControllerWithUser(null);

            var result = await controller.Search("test");

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Search_WhenQueryTooShort_ShouldReturnBadRequest()
        {
            var controller = CreateControllerWithUser("user1");

            var result = await controller.Search("a");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Search_WhenServiceThrows_ShouldReturn500()
        {
            var controller = CreateControllerWithUser("user1");

            _mockTwitchApiService
                .Setup(x => x.SearchCategoriesAsync("user1", "test", 20))
                .ThrowsAsync(new Exception("boom"));

            var result = await controller.Search("test");

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, obj.StatusCode);
        }

        [Fact]
        public async Task ListLibrary_WhenUserPresent_ShouldReturnOk()
        {
            var controller = CreateControllerWithUser("user1");

            _mockGameLibraryRepository
                .Setup(x => x.ListAsync("user1", 200))
                .ReturnsAsync(Array.Empty<GameLibraryItem>());

            var result = await controller.ListLibrary();

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task AddToLibrary_WhenMissingGameId_ShouldReturnBadRequest()
        {
            var controller = CreateControllerWithUser("user1");

            var result = await controller.AddToLibrary(new AddGameRequest { GameId = "", GameName = "Test" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetActiveGame_WhenNoContext_ShouldReturnOkWithNulls()
        {
            var controller = CreateControllerWithUser("user1");

            _mockGameContextRepository
                .Setup(x => x.GetAsync("user1"))
                .ReturnsAsync((GameContext?)null);

            var result = await controller.GetActiveGame();

            Assert.IsType<OkObjectResult>(result);
        }
    }
}
