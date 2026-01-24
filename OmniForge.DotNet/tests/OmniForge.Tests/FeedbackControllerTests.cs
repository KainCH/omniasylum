using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Models;
using OmniForge.Web.Controllers;
using OmniForge.Web.Models;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class FeedbackControllerTests
    {
        [Fact]
        public async Task CreateGitHubIssue_ShouldReturnOk_AndCallService()
        {
            var mockService = new Mock<IFeedbackIssueService>();
            mockService
                .Setup(s => s.CreateIssueAsync(
                    It.IsAny<CreateGitHubIssueRequest>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GitHubIssueCreateResult(99, "https://github.com/KainCH/omniasylum/issues/99"));

            var logger = Mock.Of<ILogger<FeedbackController>>();
            var controller = new FeedbackController(mockService.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345"),
                new Claim("displayName", "TestUser")
            }, "mock"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            var request = new CreateGitHubIssueRequest
            {
                Type = "bug",
                Title = "Something broke",
                Description = "It crashes"
            };

            var result = await controller.CreateGitHubIssue(request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);

            mockService.Verify(s => s.CreateIssueAsync(
                It.Is<CreateGitHubIssueRequest>(r => r.Type == "bug" && r.Title == "Something broke"),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<CancellationToken>()),
                Times.Once);

            Assert.NotNull(ok.Value);
        }

        [Fact]
        public async Task CreateGitHubIssue_ShouldReturnUnauthorized_WhenNoUser()
        {
            var mockService = new Mock<IFeedbackIssueService>();
            var logger = Mock.Of<ILogger<FeedbackController>>();
            var controller = new FeedbackController(mockService.Object, logger);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            };

            var request = new CreateGitHubIssueRequest
            {
                Type = "feature",
                Title = "Add thing",
                Description = "Please"
            };

            var result = await controller.CreateGitHubIssue(request, CancellationToken.None);
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task CreateGitHubIssue_WhenModelStateInvalid_ShouldReturnValidationProblem()
        {
            var mockService = new Mock<IFeedbackIssueService>();
            var logger = Mock.Of<ILogger<FeedbackController>>();
            var controller = new FeedbackController(mockService.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            controller.ModelState.AddModelError("Title", "Required");

            var result = await controller.CreateGitHubIssue(new CreateGitHubIssueRequest(), CancellationToken.None);

            Assert.IsType<ObjectResult>(result); // ValidationProblem returns ObjectResult
        }

        [Fact]
        public async Task CreateGitHubIssue_WhenTypeIsInvalid_ShouldReturnBadRequest()
        {
            var mockService = new Mock<IFeedbackIssueService>();
            mockService
                .Setup(s => s.CreateIssueAsync(It.IsAny<CreateGitHubIssueRequest>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("Invalid type", nameof(CreateGitHubIssueRequest.Type)));

            var logger = Mock.Of<ILogger<FeedbackController>>();
            var controller = new FeedbackController(mockService.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            var request = new CreateGitHubIssueRequest { Type = "nope", Title = "t", Description = "d" };
            var result = await controller.CreateGitHubIssue(request, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CreateGitHubIssue_WhenServiceThrows_ShouldReturn500()
        {
            var mockService = new Mock<IFeedbackIssueService>();
            mockService
                .Setup(s => s.CreateIssueAsync(It.IsAny<CreateGitHubIssueRequest>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("boom"));

            var logger = Mock.Of<ILogger<FeedbackController>>();
            var controller = new FeedbackController(mockService.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            var request = new CreateGitHubIssueRequest { Type = "bug", Title = "t", Description = "d" };
            var result = await controller.CreateGitHubIssue(request, CancellationToken.None);

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, obj.StatusCode);
        }
    }
}
