using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using System.Security.Claims;
using Xunit;

namespace OmniForge.Tests.Controllers
{
    public class SyncControllerDownloadTests
    {
        private static SyncController CreateController(
            Mock<IUserRepository>? userRepositoryMock = null,
            ClaimsPrincipal? user = null)
        {
            var trackerMock = new Mock<ISyncAgentTracker>(MockBehavior.Loose);
            var sceneRepoMock = new Mock<ISceneRepository>(MockBehavior.Loose);
            var sceneActionRepoMock = new Mock<ISceneActionRepository>(MockBehavior.Loose);
            userRepositoryMock ??= new Mock<IUserRepository>(MockBehavior.Loose);

            // No blob service client (null) - download will return 503
            var controller = new SyncController(
                trackerMock.Object,
                sceneRepoMock.Object,
                sceneActionRepoMock.Object,
                userRepositoryMock.Object,
                blobServiceClient: null);

            if (user != null)
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user }
                };
            }

            return controller;
        }

        private static ClaimsPrincipal CreateUserPrincipal(string userId)
        {
            var claims = new[] { new Claim("userId", userId) };
            var identity = new ClaimsIdentity(claims, "Bearer");
            return new ClaimsPrincipal(identity);
        }

        [Fact]
        public async Task Download_Unauthenticated_ReturnsUnauthorized()
        {
            var controller = CreateController(user: new ClaimsPrincipal(new ClaimsIdentity()));

            var result = await controller.DownloadAgent();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Download_FeatureDisabled_Returns403()
        {
            var userRepo = new Mock<IUserRepository>(MockBehavior.Loose);
            userRepo.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                Features = new FeatureFlags { SceneSync = false }
            });

            var principal = CreateUserPrincipal("user1");
            var controller = CreateController(userRepo, principal);

            var result = await controller.DownloadAgent();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, objectResult.StatusCode);
        }

        [Fact]
        public async Task Download_FeatureEnabled_NoBlobStorage_Returns503()
        {
            var userRepo = new Mock<IUserRepository>(MockBehavior.Loose);
            userRepo.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                Features = new FeatureFlags { SceneSync = true }
            });

            var principal = CreateUserPrincipal("user1");
            var controller = CreateController(userRepo, principal);

            var result = await controller.DownloadAgent();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(503, objectResult.StatusCode);
        }

        [Fact]
        public async Task Version_Unauthenticated_ReturnsUnauthorized()
        {
            var controller = CreateController(user: new ClaimsPrincipal(new ClaimsIdentity()));

            var result = await controller.GetAgentVersion();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Version_FeatureDisabled_Returns403()
        {
            var userRepo = new Mock<IUserRepository>(MockBehavior.Loose);
            userRepo.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                Features = new FeatureFlags { SceneSync = false }
            });

            var principal = CreateUserPrincipal("user1");
            var controller = CreateController(userRepo, principal);

            var result = await controller.GetAgentVersion();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, objectResult.StatusCode);
        }
    }
}
