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
    public class SyncControllerTests
    {
        private readonly Mock<ISyncAgentTracker> _mockTracker = new();
        private readonly Mock<ISceneRepository> _mockSceneRepo = new();
        private readonly Mock<ISceneActionRepository> _mockSceneActionRepo = new();
        private readonly Mock<IUserRepository> _mockUserRepo = new();

        private SyncController CreateController(string? userId)
        {
            var controller = new SyncController(
                _mockTracker.Object,
                _mockSceneRepo.Object,
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
        public async Task GetStatus_WhenFeatureDisabled_ShouldReturn403()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = false } });

            var result = await controller.GetStatus();

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, obj.StatusCode);
        }

        [Fact]
        public async Task GetStatus_WhenAgentConnected_ShouldReturnConnectedTrue()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = true } });
            _mockTracker.Setup(x => x.GetAgentState("user1"))
                .Returns(new AgentState { UserId = "user1", SoftwareType = "obs", CurrentScene = "Gaming" });

            var result = await controller.GetStatus();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(ok.Value);
        }

        [Fact]
        public async Task GetStatus_WhenNotAuthenticated_ShouldReturnUnauthorized()
        {
            var controller = CreateController(null);

            var result = await controller.GetStatus();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetScenes_WhenFeatureEnabled_ShouldReturnScenes()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = true } });
            _mockSceneRepo.Setup(x => x.GetScenesAsync("user1"))
                .ReturnsAsync(new System.Collections.Generic.List<Scene>
                {
                    new Scene { Name = "Gaming", Source = "obs" },
                    new Scene { Name = "BRB", Source = "obs" }
                });

            var result = await controller.GetScenes();

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task SaveSceneAction_ShouldSetUserId()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = true } });

            var action = new SceneAction { SceneName = "Gaming", TimerDurationMinutes = 30 };

            var result = await controller.SaveSceneAction(action);

            Assert.IsType<OkObjectResult>(result);
            _mockSceneActionRepo.Verify(x => x.SaveAsync(It.Is<SceneAction>(a => a.UserId == "user1" && a.SceneName == "Gaming")), Times.Once);
        }

        [Fact]
        public async Task DeleteSceneAction_ShouldCallRepository()
        {
            var controller = CreateController("user1");
            _mockUserRepo.Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { SceneSync = true } });

            var result = await controller.DeleteSceneAction("Gaming");

            Assert.IsType<NoContentResult>(result);
            _mockSceneActionRepo.Verify(x => x.DeleteAsync("user1", "Gaming"), Times.Once);
        }
    }
}
