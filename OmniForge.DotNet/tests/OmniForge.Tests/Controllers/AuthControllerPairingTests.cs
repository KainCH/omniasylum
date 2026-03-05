using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Web.Controllers;
using OmniForge.Web.Services;
using System.Collections.Generic;
using System.Security.Claims;
using Xunit;

namespace OmniForge.Tests.Controllers
{
    public class AuthControllerPairingTests
    {
        private static AuthController CreateController(
            AgentPairingService pairingService,
            Mock<IUserRepository>? userRepositoryMock = null,
            Mock<IJwtService>? jwtServiceMock = null,
            ClaimsPrincipal? user = null)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Twitch:RedirectUri"] = "https://example.test/auth/twitch/callback"
                })
                .Build();

            userRepositoryMock ??= new Mock<IUserRepository>(MockBehavior.Loose);
            jwtServiceMock ??= new Mock<IJwtService>(MockBehavior.Loose);

            var controller = new AuthController(
                new Mock<ITwitchAuthService>(MockBehavior.Loose).Object,
                userRepositoryMock.Object,
                new Mock<IBotCredentialRepository>(MockBehavior.Loose).Object,
                jwtServiceMock.Object,
                Options.Create(new TwitchSettings { ClientId = "test" }),
                config,
                new Mock<ILogger<AuthController>>(MockBehavior.Loose).Object,
                pairingService);

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
            var identity = new ClaimsIdentity(claims, "Cookies");
            return new ClaimsPrincipal(identity);
        }

        [Fact]
        public void Initiate_ValidCode_ReturnsOk()
        {
            var svc = new AgentPairingService();
            var controller = CreateController(svc);

            var result = controller.InitiatePairing(new InitiatePairingRequest("X7K2M9"));

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public void Initiate_DuplicateCode_ReturnsConflict()
        {
            var svc = new AgentPairingService();
            var controller = CreateController(svc);

            controller.InitiatePairing(new InitiatePairingRequest("X7K2M9"));
            var result = controller.InitiatePairing(new InitiatePairingRequest("X7K2M9"));

            Assert.IsType<ConflictObjectResult>(result);
        }

        [Fact]
        public void Initiate_InvalidCodeFormat_ReturnsBadRequest()
        {
            var svc = new AgentPairingService();
            var controller = CreateController(svc);

            var result = controller.InitiatePairing(new InitiatePairingRequest("bad"));

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Approve_ValidCode_ReturnsOk()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("X7K2M9", DateTimeOffset.UtcNow.AddMinutes(5));

            var userRepo = new Mock<IUserRepository>(MockBehavior.Loose);
            userRepo.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                Username = "testuser",
                Features = new FeatureFlags { SceneSync = true }
            });

            var jwtMock = new Mock<IJwtService>(MockBehavior.Loose);
            jwtMock.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("new-jwt-token");

            var principal = CreateUserPrincipal("user1");
            var controller = CreateController(svc, userRepo, jwtMock, principal);

            var result = await controller.ApprovePairing(new ApprovePairingRequest("X7K2M9"));

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task Approve_UnknownCode_ReturnsNotFound()
        {
            var svc = new AgentPairingService();

            var userRepo = new Mock<IUserRepository>(MockBehavior.Loose);
            userRepo.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                Features = new FeatureFlags { SceneSync = true }
            });

            var principal = CreateUserPrincipal("user1");
            var controller = CreateController(svc, userRepo, user: principal);

            var result = await controller.ApprovePairing(new ApprovePairingRequest("NOCODE"));

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task Approve_SceneSyncDisabled_Returns403()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("X7K2M9", DateTimeOffset.UtcNow.AddMinutes(5));

            var userRepo = new Mock<IUserRepository>(MockBehavior.Loose);
            userRepo.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                Features = new FeatureFlags { SceneSync = false }
            });

            var principal = CreateUserPrincipal("user1");
            var controller = CreateController(svc, userRepo, user: principal);

            var result = await controller.ApprovePairing(new ApprovePairingRequest("X7K2M9"));

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, objectResult.StatusCode);
        }

        [Fact]
        public void Poll_PendingCode_Returns202()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("X7K2M9", DateTimeOffset.UtcNow.AddMinutes(5));
            var controller = CreateController(svc);

            var result = controller.PollPairing("X7K2M9");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(202, objectResult.StatusCode);
        }

        [Fact]
        public void Poll_ApprovedCode_ReturnsOkWithToken()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("X7K2M9", DateTimeOffset.UtcNow.AddMinutes(5));
            svc.TryApprove("X7K2M9", "user1", "jwt-token-123");
            var controller = CreateController(svc);

            var result = controller.PollPairing("X7K2M9");

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public void Poll_ExpiredCode_Returns410()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("X7K2M9", DateTimeOffset.UtcNow.AddMinutes(-1));
            var controller = CreateController(svc);

            var result = controller.PollPairing("X7K2M9");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(410, objectResult.StatusCode);
        }
    }
}
