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

namespace OmniForge.Tests.Controllers
{
    public class BroadcastProfileControllerTests
    {
        private readonly Mock<IBroadcastProfileRepository> _mockProfileRepo = new();
        private readonly Mock<ISceneActionRepository> _mockSceneActionRepo = new();
        private readonly Mock<IUserRepository> _mockUserRepo = new();

        private BroadcastProfileController CreateController(string? userId)
        {
            var controller = new BroadcastProfileController(
                _mockProfileRepo.Object,
                _mockSceneActionRepo.Object,
                _mockUserRepo.Object);

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
        public async Task GetAll_WhenFeatureEnabled_ShouldReturnProfiles()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = true } });
            _mockProfileRepo.Setup(x => x.GetAllAsync("user1"))
                .ReturnsAsync(new List<BroadcastProfile>
                {
                    new BroadcastProfile { Name = "Competitive", ProfileId = "p1" }
                });

            var result = await controller.GetAll();

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetAll_WhenFeatureDisabled_ShouldReturn403()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = false } });

            var result = await controller.GetAll();

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, obj.StatusCode);
        }

        [Fact]
        public async Task Save_ShouldPersistProfile()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = true } });

            var profile = new BroadcastProfile { Name = "Test Profile" };

            var result = await controller.Save(profile);

            Assert.IsType<OkObjectResult>(result);
            _mockProfileRepo.Verify(x => x.SaveAsync(It.Is<BroadcastProfile>(p =>
                p.UserId == "user1" && p.Name == "Test Profile")), Times.Once);
        }

        [Fact]
        public async Task Delete_ShouldCallRepository()
        {
            var controller = CreateController("user1");

            var result = await controller.Delete("p1");

            Assert.IsType<NoContentResult>(result);
            _mockProfileRepo.Verify(x => x.DeleteAsync("user1", "p1"), Times.Once);
        }

        [Fact]
        public async Task Load_ShouldReplaceSceneActions()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = true } });

            var profile = new BroadcastProfile
            {
                UserId = "user1",
                ProfileId = "p1",
                SceneActions = new List<SceneAction>
                {
                    new SceneAction { SceneName = "Gaming", TimerDurationMinutes = 60 },
                    new SceneAction { SceneName = "BRB", TimerDurationMinutes = 5 }
                }
            };
            _mockProfileRepo.Setup(x => x.GetAsync("user1", "p1")).ReturnsAsync(profile);
            _mockSceneActionRepo.Setup(x => x.GetAllAsync("user1")).ReturnsAsync(new List<SceneAction>
            {
                new SceneAction { SceneName = "OldScene", UserId = "user1" }
            });

            var result = await controller.Load("p1");

            Assert.IsType<OkObjectResult>(result);
            _mockSceneActionRepo.Verify(x => x.DeleteAsync("user1", "OldScene"), Times.Once);
            _mockSceneActionRepo.Verify(x => x.SaveAsync(It.Is<SceneAction>(a => a.SceneName == "Gaming")), Times.Once);
            _mockSceneActionRepo.Verify(x => x.SaveAsync(It.Is<SceneAction>(a => a.SceneName == "BRB")), Times.Once);
        }
    }
}
