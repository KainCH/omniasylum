using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Models;
using OmniForge.Web.Models;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class FeedbackIssueServiceTests
    {
        [Fact]
        public async Task CreateIssueAsync_ShouldThrowUnauthorized_WhenNoUserIdClaim()
        {
            var gitHub = new Mock<IGitHubIssueService>();
            var logger = Mock.Of<ILogger<FeedbackIssueService>>();
            var service = new FeedbackIssueService(gitHub.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity());

            var request = new CreateGitHubIssueRequest
            {
                Type = "bug",
                Title = "Something broke",
                Description = "This is long enough"
            };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.CreateIssueAsync(request, user, CancellationToken.None));
        }

        [Fact]
        public async Task CreateIssueAsync_ShouldUseNameIdentifier_WhenUserIdClaimMissing()
        {
            var gitHub = new Mock<IGitHubIssueService>();
            gitHub
                .Setup(s => s.CreateIssueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GitHubIssueCreateResult(1, "https://example.invalid/issues/1"));

            var logger = Mock.Of<ILogger<FeedbackIssueService>>();
            var service = new FeedbackIssueService(gitHub.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "twitch-123")
            }, "mock"));

            var request = new CreateGitHubIssueRequest
            {
                Type = "feature",
                Title = "Add a thing",
                Description = "Please add a new thing to the app"
            };

            var result = await service.CreateIssueAsync(request, user, CancellationToken.None);
            Assert.Equal(1, result.Number);

            gitHub.Verify(s => s.CreateIssueAsync(
                It.Is<string>(t => t.StartsWith("[Feature] ", StringComparison.Ordinal) && t.Contains("Add a thing")),
                It.Is<string>(b => b.Contains("**UserId:** twitch-123")),
                It.Is<IReadOnlyCollection<string>>(labels => labels.Contains("feature") && labels.Contains("from-app")),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Theory]
        [InlineData("bug", "[Bug]")]
        [InlineData("feature", "[Feature]")]
        public async Task CreateIssueAsync_ShouldValidateType_AndPrefixTitle(string type, string expectedPrefix)
        {
            var gitHub = new Mock<IGitHubIssueService>();
            gitHub
                .Setup(s => s.CreateIssueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GitHubIssueCreateResult(99, "https://example.invalid/issues/99"));

            var logger = Mock.Of<ILogger<FeedbackIssueService>>();
            var service = new FeedbackIssueService(gitHub.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("userId", "12345"),
                new Claim("displayName", "Test Display")
            }, "mock"));

            var request = new CreateGitHubIssueRequest
            {
                Type = $"  {type.ToUpperInvariant()}  ",
                Title = "  Title with spaces  ",
                Description = "This description is definitely long enough"
            };

            await service.CreateIssueAsync(request, user, CancellationToken.None);

            gitHub.Verify(s => s.CreateIssueAsync(
                It.Is<string>(t => t.StartsWith(expectedPrefix + " ", StringComparison.Ordinal) && t.EndsWith("Title with spaces", StringComparison.Ordinal)),
                It.Is<string>(b => b.Contains("**Submitted by:** Test Display") && b.Contains("## Description")),
                It.Is<IReadOnlyCollection<string>>(labels => labels.Contains(type) && labels.Contains("from-app")),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CreateIssueAsync_ShouldIncludeOptionalSections_WhenProvided()
        {
            var gitHub = new Mock<IGitHubIssueService>();
            gitHub
                .Setup(s => s.CreateIssueAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GitHubIssueCreateResult(2, "https://example.invalid/issues/2"));

            var logger = Mock.Of<ILogger<FeedbackIssueService>>();
            var service = new FeedbackIssueService(gitHub.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            var request = new CreateGitHubIssueRequest
            {
                Type = "bug",
                Title = "Crash",
                Description = "This description is definitely long enough",
                StepsToReproduce = "Step 1",
                ExpectedBehavior = "Should work",
                ActualBehavior = "Crashes",
                AdditionalInfo = "Extra"
            };

            await service.CreateIssueAsync(request, user, CancellationToken.None);

            gitHub.Verify(s => s.CreateIssueAsync(
                It.IsAny<string>(),
                It.Is<string>(b =>
                    b.Contains("## Steps to reproduce") &&
                    b.Contains("## Expected behavior") &&
                    b.Contains("## Actual behavior") &&
                    b.Contains("## Additional info")),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CreateIssueAsync_ShouldThrowArgumentException_WhenTypeInvalid()
        {
            var gitHub = new Mock<IGitHubIssueService>();
            var logger = Mock.Of<ILogger<FeedbackIssueService>>();
            var service = new FeedbackIssueService(gitHub.Object, logger);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            var request = new CreateGitHubIssueRequest
            {
                Type = "other",
                Title = "Valid title",
                Description = "This description is definitely long enough"
            };

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CreateIssueAsync(request, user, CancellationToken.None));

            Assert.Equal(nameof(CreateGitHubIssueRequest.Type), ex.ParamName);
        }
    }
}
