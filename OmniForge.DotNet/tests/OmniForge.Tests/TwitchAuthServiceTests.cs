using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging; // Added
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class TwitchAuthServiceTests
    {
        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private readonly Mock<IOptions<TwitchSettings>> _mockOptions;
        private readonly Mock<ILogger<TwitchAuthService>> _mockLogger; // Added
        private readonly TwitchAuthService _service;
        private readonly HttpClient _httpClient;
        private readonly TwitchSettings _settings;

        public TwitchAuthServiceTests()
        {
            _settings = new TwitchSettings
            {
                ClientId = "test_client_id",
                ClientSecret = "test_client_secret",
                RedirectUri = "http://localhost/callback"
            };

            _mockOptions = new Mock<IOptions<TwitchSettings>>();
            _mockOptions.Setup(x => x.Value).Returns(_settings);

            _mockLogger = new Mock<ILogger<TwitchAuthService>>(); // Added

            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_mockHttpMessageHandler.Object);

            _service = new TwitchAuthService(_httpClient, _mockOptions.Object, _mockLogger.Object); // Updated
        }

        [Fact]
        public void GetAuthorizationUrl_ShouldReturnCorrectUrl()
        {
            var redirectUri = "http://localhost/callback";
            var url = _service.GetAuthorizationUrl(redirectUri);

            Assert.Contains("https://id.twitch.tv/oauth2/authorize", url);
            Assert.Contains($"client_id={_settings.ClientId}", url);
            Assert.Contains($"redirect_uri={System.Net.WebUtility.UrlEncode(redirectUri)}", url);
            Assert.Contains("response_type=code", url);
            Assert.Contains("scope=", url);
        }

        [Fact]
        public async Task ExchangeCodeForTokenAsync_ShouldReturnToken_WhenSuccess()
        {
            var code = "test_code";
            var redirectUri = "http://localhost/callback";
            var jsonResponse = "{\"access_token\":\"access123\",\"refresh_token\":\"refresh123\",\"expires_in\":3600,\"token_type\":\"bearer\"}";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.Method == HttpMethod.Post &&
                        req.RequestUri != null &&
                        req.RequestUri.ToString() == "https://id.twitch.tv/oauth2/token"),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(jsonResponse)
                });

            var result = await _service.ExchangeCodeForTokenAsync(code, redirectUri);

            Assert.NotNull(result);
            var tokenResponse = result!;
            Assert.Equal("access123", tokenResponse.AccessToken);
            Assert.Equal("refresh123", tokenResponse.RefreshToken);
            Assert.Equal(3600, tokenResponse.ExpiresIn);
        }

        [Fact]
        public async Task ExchangeCodeForTokenAsync_ShouldReturnNull_WhenFailure()
        {
            var code = "test_code";
            var redirectUri = "http://localhost/callback";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest
                });

            var result = await _service.ExchangeCodeForTokenAsync(code, redirectUri);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetUserInfoAsync_ShouldReturnUser_WhenSuccess()
        {
            var accessToken = "access123";
            var clientId = "client123";
            var jsonResponse = "{\"data\":[{\"id\":\"123\",\"login\":\"testuser\",\"display_name\":\"Test User\",\"email\":\"test@example.com\",\"profile_image_url\":\"http://image.url\"}]}";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.Method == HttpMethod.Get &&
                        req.RequestUri != null &&
                        req.RequestUri.ToString() == "https://api.twitch.tv/helix/users"),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(jsonResponse)
                });

            var result = await _service.GetUserInfoAsync(accessToken, clientId);

            Assert.NotNull(result);
            Assert.Equal("123", result!.Id);
            Assert.Equal("testuser", result!.Login);
        }

        [Fact]
        public async Task GetUserInfoAsync_ShouldReturnNull_WhenFailure()
        {
            var accessToken = "access123";
            var clientId = "client123";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Unauthorized
                });

            var result = await _service.GetUserInfoAsync(accessToken, clientId);

            Assert.Null(result);
        }

        [Fact]
        public async Task RefreshTokenAsync_ShouldReturnToken_WhenSuccess()
        {
            var refreshToken = "refresh123";
            var jsonResponse = "{\"access_token\":\"new_access\",\"refresh_token\":\"new_refresh\",\"expires_in\":3600,\"token_type\":\"bearer\"}";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.Method == HttpMethod.Post &&
                        req.RequestUri != null &&
                        req.RequestUri.ToString() == "https://id.twitch.tv/oauth2/token"),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(jsonResponse)
                });

            var result = await _service.RefreshTokenAsync(refreshToken);

            Assert.NotNull(result);
            Assert.Equal("new_access", result!.AccessToken);
            Assert.Equal("new_refresh", result!.RefreshToken);
        }

        [Fact]
        public async Task RefreshTokenAsync_ShouldReturnNull_WhenFailure()
        {
            var refreshToken = "refresh123";

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest
                });

            var result = await _service.RefreshTokenAsync(refreshToken);

            Assert.Null(result);
        }
    }
}
