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

        [Fact]
        public async Task CreateIssueAsync_ShouldThrow_WhenGitHubReturnsNonSuccess()
        {
            var handler = new Mock<HttpMessageHandler>();

            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Unauthorized,
                    Content = new StringContent("{\"message\":\"bad credentials\"}", Encoding.UTF8, "application/json")
                });

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

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateIssueAsync("Test title", "Test body", new List<string> { "bug" }, CancellationToken.None));
        }

        [Fact]
        public async Task CreateIssueAsync_ShouldPropagateHttpRequestException()
        {
            var handler = new Mock<HttpMessageHandler>();

            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("network down"));

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

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.CreateIssueAsync("Test title", "Test body", new List<string>(), CancellationToken.None));
        }

        [Fact]
        public async Task CreateIssueAsync_ShouldThrow_WhenResponseJsonMalformed()
        {
            var handler = new Mock<HttpMessageHandler>();

            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Created,
                    Content = new StringContent("{", Encoding.UTF8, "application/json")
                });

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

            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
                service.CreateIssueAsync("Test title", "Test body", new List<string>(), CancellationToken.None));
        }

        [Fact]
        public async Task CreateIssueAsync_ShouldPropagateCancellationToken_ToHttpClient()
        {
            var handler = new Mock<HttpMessageHandler>();

            CancellationToken observedToken = default;

            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((_, ct) => observedToken = ct)
                .Returns<HttpRequestMessage, CancellationToken>((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));

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

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.CreateIssueAsync("Test title", "Test body", new List<string>(), cts.Token));

            Assert.Equal(cts.Token, observedToken);
        }
    }
}
