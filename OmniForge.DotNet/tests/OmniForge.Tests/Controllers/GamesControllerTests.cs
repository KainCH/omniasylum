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

        [Fact]
        public async Task AddToLibrary_WhenNoUserId_ShouldReturnUnauthorized()
        {
            var controller = CreateControllerWithUser(null);

            var result = await controller.AddToLibrary(new AddGameRequest { GameId = "1", GameName = "Test" });

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task AddToLibrary_WhenValidRequest_ShouldUpsertTrimmedValues()
        {
            var controller = CreateControllerWithUser("user1");

            GameLibraryItem? captured = null;
            _mockGameLibraryRepository
                .Setup(x => x.UpsertAsync(It.IsAny<GameLibraryItem>()))
                .Callback<GameLibraryItem>(g => captured = g)
                .Returns(Task.CompletedTask);

            var result = await controller.AddToLibrary(new AddGameRequest
            {
                GameId = " 123 ",
                GameName = " Test Game ",
                BoxArtUrl = " https://example/box.png "
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(captured);
            Assert.Equal("user1", captured!.UserId);
            Assert.Equal("123", captured.GameId);
            Assert.Equal("Test Game", captured.GameName);
            Assert.Equal("https://example/box.png", captured.BoxArtUrl);
        }

        [Fact]
        public async Task Search_WhenValidQuery_ShouldReturnOk()
        {
            var controller = CreateControllerWithUser("user1");

            _mockTwitchApiService
                .Setup(x => x.SearchCategoriesAsync("user1", "test", 20))
                .ReturnsAsync(new[] { new TwitchCategoryDto { Id = "1", Name = "Test" } });

            var result = await controller.Search("test");

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task ListLibrary_WhenNoUserId_ShouldReturnUnauthorized()
        {
            var controller = CreateControllerWithUser(null);

            var result = await controller.ListLibrary();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetActiveGame_WhenNoUserId_ShouldReturnUnauthorized()
        {
            var controller = CreateControllerWithUser(null);

            var result = await controller.GetActiveGame();

            Assert.IsType<UnauthorizedResult>(result);
        }
    }
}
