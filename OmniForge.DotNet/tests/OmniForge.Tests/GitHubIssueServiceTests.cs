using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using OmniForge.Core.Models;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class GitHubIssueServiceTests
    {
        [Fact]
        public async Task CreateIssueAsync_ShouldPostToGitHubIssuesEndpoint()
        {
            var handler = new Mock<HttpMessageHandler>();

            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.Method == HttpMethod.Post &&
                        req.RequestUri != null &&
                        req.RequestUri.ToString() == "https://api.github.com/repos/KainCH/omniasylum/issues" &&
                        req.Headers.UserAgent.ToString().Contains("OmniForge") &&
                        req.Headers.Authorization != null &&
                        req.Headers.Authorization.Scheme == "token"),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Created,
                    Content = new StringContent("{\"number\": 42, \"html_url\": \"https://github.com/KainCH/omniasylum/issues/42\"}", Encoding.UTF8, "application/json")
                })
                .Verifiable();

            var httpClient = new HttpClient(handler.Object);
            var settings = Mock.Of<IOptionsMonitor<GitHubSettings>>(m =>
                m.CurrentValue == new GitHubSettings
                {
                    ApiBaseUrl = "https://api.github.com",
                    IssuesToken = "test-token",
                    RepoOwner = "KainCH",
                    RepoName = "omniasylum"
                });

            var logger = Mock.Of<ILogger<GitHubIssueService>>();
            var service = new GitHubIssueService(httpClient, settings, logger);

            var result = await service.CreateIssueAsync(
                "Test title",
                "Test body",
                new List<string> { "bug", "from-app" },
                CancellationToken.None);

            Assert.Equal(42, result.Number);
            Assert.Equal("https://github.com/KainCH/omniasylum/issues/42", result.HtmlUrl);

            handler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task CreateIssueAsync_ShouldThrow_WhenTokenMissing()
        {
            var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
            var settings = Mock.Of<IOptionsMonitor<GitHubSettings>>(m =>
                m.CurrentValue == new GitHubSettings
                {
                    ApiBaseUrl = "https://api.github.com",
                    IssuesToken = "",
                    RepoOwner = "KainCH",
                    RepoName = "omniasylum"
                });

            var logger = Mock.Of<ILogger<GitHubIssueService>>();
            var service = new GitHubIssueService(httpClient, settings, logger);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateIssueAsync("t", "b", new List<string>()));
        }
    }
}
