using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Entities;
using OmniForge.Core.Exceptions;
using OmniForge.Infrastructure.Configuration;
using TwitchLib.Api.Helix.Models.Moderation.AutomodSettings;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Services;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using Xunit;
using System.Text.Json;
using System.Reflection;

namespace OmniForge.Tests
{
    public class TwitchApiServiceTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ITwitchAuthService> _mockAuthService;
        private readonly Mock<IBotCredentialRepository> _mockBotCredentialRepository;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<ITwitchHelixWrapper> _mockHelixWrapper;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<ILogger<TwitchApiService>> _mockLogger;
        private readonly IOptions<TwitchSettings> _twitchSettings;
        private readonly TwitchApiService _service;

        public TwitchApiServiceTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockAuthService = new Mock<ITwitchAuthService>();
            _mockBotCredentialRepository = new Mock<IBotCredentialRepository>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockHelixWrapper = new Mock<ITwitchHelixWrapper>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockLogger = new Mock<ILogger<TwitchApiService>>();

            _twitchSettings = Options.Create(new TwitchSettings
            {
                ClientId = "test_client_id"
            });

            _mockConfiguration.Setup(x => x["Twitch:ClientId"]).Returns("test_client_id");

            _service = new TwitchApiService(
                _mockUserRepository.Object,
                _mockAuthService.Object,
                _mockBotCredentialRepository.Object,
                _mockConfiguration.Object,
                _twitchSettings,
                _mockHelixWrapper.Object,
                _mockHttpClientFactory.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task GetModeratorsAsync_ShouldParseUserLogin_FromHelixResponse()
        {
            var json = "{\"data\":[{\"user_id\":\"424596340\",\"user_login\":\"omniforge_bot\",\"user_name\":\"OmniForge_Bot\"}],\"pagination\":{}}";

            var handler = new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });

            using var httpClient = new HttpClient(handler);
            _mockHttpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            var response = await _service.GetModeratorsAsync("125828897", "token", CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(response.Moderators);
            Assert.Equal("omniforge_bot", response.Moderators[0].UserLogin);
            Assert.NotNull(response.FindModeratorByUserIdOrLogin("omniforge_bot"));
        }

        [Fact]
        public async Task SendChatMessageAsBotAsync_WhenBotTokenValid_ShouldSendWithBotTokenAndSenderId()
        {
            _mockConfiguration.Setup(x => x["Twitch:ClientId"]).Returns("test_client_id");

            _mockBotCredentialRepository
                .Setup(x => x.GetAsync())
                .ReturnsAsync(new BotCredentials
                {
                    Username = "omniforge_bot",
                    AccessToken = "bot_access",
                    RefreshToken = "bot_refresh",
                    TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
                });

            _mockAuthService
                .Setup(x => x.RefreshTokenAsync(It.IsAny<string>()))
                .Throws(new Exception("Refresh should not be called"));

            string? capturedBearer = null;
            string? capturedClientId = null;
            string? capturedJson = null;

            var handler = new StubHttpMessageHandler(req =>
            {
                capturedBearer = req.Headers.Authorization?.Parameter;
                capturedClientId = req.Headers.TryGetValues("Client-Id", out var values) ? values.FirstOrDefault() : null;
                capturedJson = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            _mockHttpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            await _service.SendChatMessageAsBotAsync("broadcaster1", "bot-user-id", "hello", replyParentMessageId: "parent1");

            Assert.Equal("bot_access", capturedBearer);
            Assert.Equal("test_client_id", capturedClientId);
            Assert.NotNull(capturedJson);

            using var doc = JsonDocument.Parse(capturedJson!);
            Assert.Equal("broadcaster1", doc.RootElement.GetProperty("broadcaster_id").GetString());
            Assert.Equal("bot-user-id", doc.RootElement.GetProperty("sender_id").GetString());
            Assert.Equal("hello", doc.RootElement.GetProperty("message").GetString());
            Assert.Equal("parent1", doc.RootElement.GetProperty("reply_parent_message_id").GetString());

            _mockBotCredentialRepository.Verify(x => x.SaveAsync(It.IsAny<BotCredentials>()), Times.Never);
        }

        [Fact]
        public async Task SendChatMessageAsBotAsync_WhenBotTokenExpiring_ShouldRefreshAndPersistThenSend()
        {
            _mockConfiguration.Setup(x => x["Twitch:ClientId"]).Returns("test_client_id");

            var botCreds = new BotCredentials
            {
                Username = "omniforge_bot",
                AccessToken = "stale_access",
                RefreshToken = "bot_refresh",
                TokenExpiry = DateTimeOffset.UtcNow.AddMinutes(1)
            };

            _mockBotCredentialRepository
                .Setup(x => x.GetAsync())
                .ReturnsAsync(botCreds);

            _mockAuthService
                .Setup(x => x.RefreshTokenAsync("bot_refresh"))
                .ReturnsAsync(new TwitchTokenResponse
                {
                    AccessToken = "new_access",
                    RefreshToken = "new_refresh",
                    ExpiresIn = 3600,
                    TokenType = "bearer"
                });

            _mockAuthService
                .Setup(x => x.GetAppAccessTokenAsync(It.Is<IReadOnlyCollection<string>?>(s => s != null && s.Contains("user:write:chat"))))
                .ReturnsAsync("app_access_chat");

            BotCredentials? savedCreds = null;
            _mockBotCredentialRepository
                .Setup(x => x.SaveAsync(It.IsAny<BotCredentials>()))
                .Callback<BotCredentials>(c => savedCreds = c)
                .Returns(Task.CompletedTask);

            string? capturedBearer = null;
            var handler = new StubHttpMessageHandler(req =>
            {
                capturedBearer = req.Headers.Authorization?.Parameter;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            _mockHttpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            await _service.SendChatMessageAsBotAsync("broadcaster1", "bot-user-id", "hello");

            Assert.Equal("app_access_chat", capturedBearer);
            Assert.NotNull(savedCreds);
            Assert.Equal("new_access", savedCreds!.AccessToken);
            Assert.Equal("new_refresh", savedCreds.RefreshToken);
            Assert.True(savedCreds.TokenExpiry > DateTimeOffset.UtcNow.AddMinutes(30));
        }

        [Fact]
        public async Task SearchCategoriesAsync_WhenAppTokenAvailable_ShouldUseAppTokenWithoutUserLookup()
        {
            _mockConfiguration.Setup(x => x["Twitch:ClientId"]).Returns("test_client_id");

            _mockAuthService
                .Setup(x => x.GetAppAccessTokenAsync(It.IsAny<IReadOnlyCollection<string>?>()))
                .ReturnsAsync("app_access");

            string? capturedBearer = null;
            var json = "{\"data\":[{\"id\":\"1\",\"name\":\"Test Game\",\"box_art_url\":\"http://example/{width}x{height}.jpg\"}],\"pagination\":{}}";

            var handler = new StubHttpMessageHandler(req =>
            {
                capturedBearer = req.Headers.Authorization?.Parameter;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            _mockHttpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            var results = await _service.SearchCategoriesAsync("user1", "test", first: 5);

            Assert.Single(results);
            Assert.Equal("app_access", capturedBearer);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task UpdateChannelInformationAsync_ShouldNotSendMoreThanSixCcls_AndShouldExcludeUnsupportedIds()
        {
            _mockUserRepository
                .Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "user1",
                    Username = "user1",
                    AccessToken = "user_access",
                    RefreshToken = "refresh",
                    TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
                });

            _mockAuthService
                .Setup(x => x.GetTokenScopesAsync("user_access"))
                .ReturnsAsync(new List<string> { "channel:manage:broadcast" });

            string? patchJson = null;

            var getJson = "{\"data\":[{\"broadcaster_id\":\"user1\",\"game_id\":\"old\",\"game_name\":\"Old\",\"content_classification_labels\":[\"Gambling\"]}]}";

            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(getJson, Encoding.UTF8, "application/json")
                    };
                }

                if (req.Method.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase))
                {
                    patchJson = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                    return new HttpResponseMessage(HttpStatusCode.NoContent)
                    {
                        Content = new StringContent(string.Empty)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            });

            using var httpClient = new HttpClient(handler);
            _mockHttpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            await _service.UpdateChannelInformationAsync(
                "user1",
                "newGame",
                new List<string> { "MatureGame", "Gambling" });

            Assert.NotNull(patchJson);

            using var doc = JsonDocument.Parse(patchJson!);
            Assert.Equal("newGame", doc.RootElement.GetProperty("game_id").GetString());

            var ccls = doc.RootElement.GetProperty("content_classification_labels");
            Assert.Equal(JsonValueKind.Array, ccls.ValueKind);

            // Helix constraint: must be <= 6.
            Assert.True(ccls.GetArrayLength() <= 6);

            var ids = ccls.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
            Assert.DoesNotContain("MatureGame", ids);
            Assert.Contains("Gambling", ids);

            var gambling = ccls.EnumerateArray().First(e => e.GetProperty("id").GetString() == "Gambling");
            Assert.True(gambling.GetProperty("is_enabled").GetBoolean());
        }

        [Fact]
        public async Task UpdateChannelInformationAsync_WhenOnlyUnknownCclsRequested_ShouldOmitCclsFromPayload()
        {
            _mockUserRepository
                .Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "user1",
                    Username = "user1",
                    AccessToken = "user_access",
                    RefreshToken = "refresh",
                    TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
                });

            _mockAuthService
                .Setup(x => x.GetTokenScopesAsync("user_access"))
                .ReturnsAsync(new List<string> { "channel:manage:broadcast" });

            string? patchJson = null;

            var getJson = "{\"data\":[{\"broadcaster_id\":\"user1\",\"game_id\":\"old\",\"game_name\":\"Old\",\"content_classification_labels\":[\"Gambling\"]}]}";

            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(getJson, Encoding.UTF8, "application/json")
                    };
                }

                if (req.Method.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase))
                {
                    patchJson = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                    return new HttpResponseMessage(HttpStatusCode.NoContent)
                    {
                        Content = new StringContent(string.Empty)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            });

            using var httpClient = new HttpClient(handler);
            _mockHttpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            await _service.UpdateChannelInformationAsync(
                "user1",
                "newGame",
                new List<string> { "MatureGame" });

            Assert.NotNull(patchJson);

            using var doc = JsonDocument.Parse(patchJson!);
            Assert.Equal("newGame", doc.RootElement.GetProperty("game_id").GetString());
            Assert.False(doc.RootElement.TryGetProperty("content_classification_labels", out _));
        }

        [Fact]
        public async Task GetChannelCategoryAsync_WhenAppTokenAvailable_ShouldUseAppTokenWithoutUserLookup()
        {
            _mockConfiguration.Setup(x => x["Twitch:ClientId"]).Returns("test_client_id");

            _mockAuthService
                .Setup(x => x.GetAppAccessTokenAsync(It.IsAny<IReadOnlyCollection<string>?>()))
                .ReturnsAsync("app_access");

            string? capturedBearer = null;
            var json = "{\"data\":[{\"broadcaster_id\":\"user1\",\"game_id\":\"123\",\"game_name\":\"Test Game\"}] }";

            var handler = new StubHttpMessageHandler(req =>
            {
                capturedBearer = req.Headers.Authorization?.Parameter;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            _mockHttpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            var result = await _service.GetChannelCategoryAsync("user1");

            Assert.NotNull(result);
            Assert.Equal("app_access", capturedBearer);
            Assert.Equal("123", result!.GameId);
            Assert.Equal("Test Game", result.GameName);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetChannelCategoryAsync_WhenResponseIncludesStringCcls_ShouldNotThrow()
        {
            _mockConfiguration.Setup(x => x["Twitch:ClientId"]).Returns("test_client_id");

            _mockAuthService
                .Setup(x => x.GetAppAccessTokenAsync(It.IsAny<IReadOnlyCollection<string>?>()))
                .ReturnsAsync("app_access");

            var json = "{\"data\":[{\"broadcaster_id\":\"user1\",\"game_id\":\"123\",\"game_name\":\"Test Game\",\"content_classification_labels\":[\"Gambling\",\"ProfanityVulgarity\"]}] }";

            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

            using var httpClient = new HttpClient(handler);
            _mockHttpClientFactory
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            var result = await _service.GetChannelCategoryAsync("user1");

            Assert.NotNull(result);
            Assert.Equal("123", result!.GameId);
            Assert.Equal("Test Game", result.GameName);
        }

        [Fact]
        public async Task GetUserByLoginAsync_WhenAppTokenAvailable_ShouldUseAppTokenWithoutUserLookup()
        {
            _mockConfiguration.Setup(x => x["Twitch:ClientId"]).Returns("test_client_id");

            _mockAuthService
                .Setup(x => x.GetAppAccessTokenAsync(It.IsAny<IReadOnlyCollection<string>?>()))
                .ReturnsAsync("app_access");

            string? capturedToken = null;
            _mockHelixWrapper
                .Setup(x => x.GetUsersAsync(
                    "test_client_id",
                    It.IsAny<string>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<List<string>?>()))
                .Callback<string, string, List<string>?, List<string>?>((_, token, _, _) => capturedToken = token)
                .ReturnsAsync(CreateGetUsersResponse(new (string Property, object? Value)[]
                {
                    ("Users", new[]
                    {
                        CreateUser(new (string Property, object? Value)[]
                        {
                            ("Id", "u1"),
                            ("Login", "some_login"),
                            ("DisplayName", "Some Login"),
                            ("ProfileImageUrl", "http://img"),
                            ("Email", "")
                        })
                    })
                }));

            var user = await _service.GetUserByLoginAsync("some_login", "acting_user");

            Assert.NotNull(user);
            Assert.Equal("app_access", capturedToken);
            Assert.Equal("u1", user!.Id);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        private static TwitchLib.Api.Helix.Models.Users.GetUsers.GetUsersResponse CreateGetUsersResponse(
            IReadOnlyCollection<(string Property, object? Value)> values)
        {
            var response = (TwitchLib.Api.Helix.Models.Users.GetUsers.GetUsersResponse)
                Activator.CreateInstance(typeof(TwitchLib.Api.Helix.Models.Users.GetUsers.GetUsersResponse), nonPublic: true)!;

            foreach (var (property, value) in values)
            {
                SetNonPublicProperty(response, property, value);
            }

            return response;
        }

        private static TwitchLib.Api.Helix.Models.Users.GetUsers.User CreateUser(
            IReadOnlyCollection<(string Property, object? Value)> values)
        {
            var user = (TwitchLib.Api.Helix.Models.Users.GetUsers.User)
                Activator.CreateInstance(typeof(TwitchLib.Api.Helix.Models.Users.GetUsers.User), nonPublic: true)!;

            foreach (var (property, value) in values)
            {
                SetNonPublicProperty(user, property, value);
            }

            return user;
        }

        private static void SetNonPublicProperty(object target, string propertyName, object? value)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                throw new InvalidOperationException($"Property '{propertyName}' not found on type '{target.GetType().FullName}'.");
            }

            var setMethod = property.GetSetMethod(nonPublic: true);
            if (setMethod == null)
            {
                throw new InvalidOperationException($"Property '{propertyName}' on type '{target.GetType().FullName}' has no setter.");
            }

            setMethod.Invoke(target, new[] { value });
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_handler(request));
            }
        }

        [Fact]
        public async Task GetAutomodSettingsAsync_ShouldUseTokenUserIdAsModerator()
        {
            var userId = "broadcaster123";
            var tokenUserId = "mod999";
            var accessToken = "token";

            var validation = new TokenValidationResult
            {
                UserId = tokenUserId,
                Scopes = new[] { "moderator:read:automod_settings" }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(new User { TwitchUserId = userId, AccessToken = accessToken, TokenExpiry = DateTimeOffset.UtcNow.AddHours(1) });
            _mockAuthService.Setup(x => x.ValidateTokenAsync(accessToken)).ReturnsAsync(validation);

            var helixResponse = new GetAutomodSettingsResponse();
            var getDataProp = typeof(GetAutomodSettingsResponse).GetProperty("Data");
            getDataProp!.SetValue(helixResponse, new[] { new TwitchLib.Api.Helix.Models.Moderation.AutomodSettings.AutomodSettingsResponseModel { OverallLevel = 2 } });

            // Token user mismatch should be rejected (prevents confusing BadScope/BadCreds errors)
            await Assert.ThrowsAsync<ReauthRequiredException>(() => _service.GetAutomodSettingsAsync(userId));
        }

        [Fact]
        public async Task UpdateAutomodSettingsAsync_ShouldUseTokenUserIdAsModerator()
        {
            var userId = "broadcaster123";
            var tokenUserId = "mod999";
            var accessToken = "token";

            var validation = new TokenValidationResult
            {
                UserId = tokenUserId,
                Scopes = new[] { "moderator:manage:automod" }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(new User { TwitchUserId = userId, AccessToken = accessToken, TokenExpiry = DateTimeOffset.UtcNow.AddHours(1) });
            _mockAuthService.Setup(x => x.ValidateTokenAsync(accessToken)).ReturnsAsync(validation);

            var dto = new AutomodSettingsDto { OverallLevel = 3 };
            var helixResponse = new UpdateAutomodSettingsResponse();
            var updateDataProp = typeof(UpdateAutomodSettingsResponse).GetProperty("Data");
            updateDataProp!.SetValue(helixResponse, new[] { new AutomodSettings { OverallLevel = 3 } });

            await Assert.ThrowsAsync<ReauthRequiredException>(() => _service.UpdateAutomodSettingsAsync(userId, dto));
        }

        [Fact]
        public async Task GetAutomodSettingsAsync_ShouldUseUserIdAsModerator_WhenTokenUserMatches()
        {
            var userId = "broadcaster123";
            var accessToken = "token";

            var validation = new TokenValidationResult
            {
                UserId = userId,
                Scopes = new[] { "moderator:read:automod_settings" }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(new User { TwitchUserId = userId, AccessToken = accessToken, TokenExpiry = DateTimeOffset.UtcNow.AddHours(1) });
            _mockAuthService.Setup(x => x.ValidateTokenAsync(accessToken)).ReturnsAsync(validation);

            var helixResponse = new GetAutomodSettingsResponse();
            var getDataProp = typeof(GetAutomodSettingsResponse).GetProperty("Data");
            getDataProp!.SetValue(helixResponse, new[] { new TwitchLib.Api.Helix.Models.Moderation.AutomodSettings.AutomodSettingsResponseModel { OverallLevel = 2 } });
            _mockHelixWrapper.Setup(x => x.GetAutomodSettingsAsync("test_client_id", accessToken, userId, userId))
                .ReturnsAsync(helixResponse);

            var result = await _service.GetAutomodSettingsAsync(userId);

            Assert.Equal(2, result.OverallLevel);
            _mockHelixWrapper.Verify(x => x.GetAutomodSettingsAsync("test_client_id", accessToken, userId, userId), Times.Once);
        }

        [Fact]
        public async Task UpdateAutomodSettingsAsync_ShouldUseUserIdAsModerator_WhenTokenUserMatches()
        {
            var userId = "broadcaster123";
            var accessToken = "token";

            var validation = new TokenValidationResult
            {
                UserId = userId,
                Scopes = new[] { "moderator:manage:automod" }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(new User { TwitchUserId = userId, AccessToken = accessToken, TokenExpiry = DateTimeOffset.UtcNow.AddHours(1) });
            _mockAuthService.Setup(x => x.ValidateTokenAsync(accessToken)).ReturnsAsync(validation);

            var dto = new AutomodSettingsDto { OverallLevel = 3 };
            var helixResponse = new UpdateAutomodSettingsResponse();
            var updateDataProp = typeof(UpdateAutomodSettingsResponse).GetProperty("Data");
            updateDataProp!.SetValue(helixResponse, new[] { new AutomodSettings { OverallLevel = 3 } });

            AutomodSettings? captured = null;
            _mockHelixWrapper
                .Setup(x => x.UpdateAutomodSettingsAsync("test_client_id", accessToken, userId, userId, It.IsAny<AutomodSettings>()))
                .Callback<string, string, string, string, AutomodSettings>((_, _, _, _, s) => captured = s)
                .ReturnsAsync(helixResponse);

            var result = await _service.UpdateAutomodSettingsAsync(userId, dto);

            Assert.Equal(3, result.OverallLevel);
            Assert.NotNull(captured);
            Assert.Equal(3, captured!.OverallLevel);
            Assert.Null(captured.Aggression);
            Assert.Null(captured.Bullying);
            Assert.Null(captured.Disability);
            Assert.Null(captured.Misogyny);
            Assert.Null(captured.RaceEthnicityOrReligion);
            Assert.Null(captured.SexBasedTerms);
            Assert.Null(captured.SexualitySexOrGender);
            Assert.Null(captured.Swearing);
            _mockHelixWrapper.Verify(x => x.UpdateAutomodSettingsAsync("test_client_id", accessToken, userId, userId, It.IsAny<AutomodSettings>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAutomodSettingsAsync_ShouldAllowManageAutomodSettingsScope_WhenTokenUserMatches()
        {
            var userId = "broadcaster123";
            var accessToken = "token";

            var validation = new TokenValidationResult
            {
                UserId = userId,
                Scopes = new[] { "moderator:manage:automod_settings" }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(new User { TwitchUserId = userId, AccessToken = accessToken, TokenExpiry = DateTimeOffset.UtcNow.AddHours(1) });
            _mockAuthService.Setup(x => x.ValidateTokenAsync(accessToken)).ReturnsAsync(validation);

            var dto = new AutomodSettingsDto { OverallLevel = 2 };
            var helixResponse = new UpdateAutomodSettingsResponse();
            var updateDataProp = typeof(UpdateAutomodSettingsResponse).GetProperty("Data");
            updateDataProp!.SetValue(helixResponse, new[] { new AutomodSettings { OverallLevel = 2 } });

            _mockHelixWrapper
                .Setup(x => x.UpdateAutomodSettingsAsync("test_client_id", accessToken, userId, userId, It.IsAny<AutomodSettings>()))
                .ReturnsAsync(helixResponse);

            var result = await _service.UpdateAutomodSettingsAsync(userId, dto);

            Assert.Equal(2, result.OverallLevel);
            _mockHelixWrapper.Verify(x => x.UpdateAutomodSettingsAsync("test_client_id", accessToken, userId, userId, It.IsAny<AutomodSettings>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAutomodSettingsAsync_ShouldSendIndividualSettings_WhenOverallLevelIsNull()
        {
            var userId = "broadcaster123";
            var accessToken = "token";

            var validation = new TokenValidationResult
            {
                UserId = userId,
                Scopes = new[] { "moderator:manage:automod" }
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(new User { TwitchUserId = userId, AccessToken = accessToken, TokenExpiry = DateTimeOffset.UtcNow.AddHours(1) });
            _mockAuthService.Setup(x => x.ValidateTokenAsync(accessToken)).ReturnsAsync(validation);

            var dto = new AutomodSettingsDto
            {
                OverallLevel = null,
                Aggression = 1,
                Bullying = 2,
                Disability = 3,
                Misogyny = 4,
                RaceEthnicityOrReligion = 0,
                SexBasedTerms = 1,
                SexualitySexOrGender = 2,
                Swearing = 3
            };

            var helixResponse = new UpdateAutomodSettingsResponse();
            var updateDataProp = typeof(UpdateAutomodSettingsResponse).GetProperty("Data");
            updateDataProp!.SetValue(helixResponse, new[] { new AutomodSettings { OverallLevel = null } });

            AutomodSettings? captured = null;
            _mockHelixWrapper
                .Setup(x => x.UpdateAutomodSettingsAsync("test_client_id", accessToken, userId, userId, It.IsAny<AutomodSettings>()))
                .Callback<string, string, string, string, AutomodSettings>((_, _, _, _, s) => captured = s)
                .ReturnsAsync(helixResponse);

            await _service.UpdateAutomodSettingsAsync(userId, dto);

            Assert.NotNull(captured);
            Assert.Null(captured!.OverallLevel);
            Assert.Equal(1, captured.Aggression);
            Assert.Equal(2, captured.Bullying);
            Assert.Equal(3, captured.Disability);
            Assert.Equal(4, captured.Misogyny);
            Assert.Equal(0, captured.RaceEthnicityOrReligion);
            Assert.Equal(1, captured.SexBasedTerms);
            Assert.Equal(2, captured.SexualitySexOrGender);
            Assert.Equal(3, captured.Swearing);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldReturnRewards_WhenUserExists()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "access_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var rewards = new List<HelixCustomReward>
            {
                new HelixCustomReward { Id = "r1", Title = "Reward 1", Cost = 100, IsEnabled = true }
            };

            _mockHelixWrapper.Setup(x => x.GetCustomRewardsAsync("test_client_id", "access_token", userId))
                .ReturnsAsync(rewards);

            var result = await _service.GetCustomRewardsAsync(userId);

            Assert.Single(result);
            Assert.Equal("r1", result.First().Id);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldRefreshToken_WhenExpired()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "old_token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddMinutes(-1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var newToken = new TwitchTokenResponse
            {
                AccessToken = "new_token",
                RefreshToken = "new_refresh",
                ExpiresIn = 3600
            };

            _mockAuthService.Setup(x => x.RefreshTokenAsync("refresh_token")).ReturnsAsync(newToken);

            var rewards = new List<HelixCustomReward>();

            _mockHelixWrapper.Setup(x => x.GetCustomRewardsAsync("test_client_id", "new_token", userId))
                .ReturnsAsync(rewards);

            await _service.GetCustomRewardsAsync(userId);

            _mockAuthService.Verify(x => x.RefreshTokenAsync("refresh_token"), Times.Once);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.AccessToken == "new_token")), Times.Once);
        }

        [Fact]
        public async Task CreateCustomRewardAsync_ShouldCreateReward_WhenValid()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "access_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var request = new CreateRewardRequest
            {
                Title = "New Reward",
                Cost = 500
            };

            var createdReward = new HelixCustomReward
            {
                Id = "new_r",
                Title = "New Reward",
                Cost = 500,
                IsEnabled = true
            };

            _mockHelixWrapper.Setup(x => x.CreateCustomRewardAsync(
                "test_client_id",
                "access_token",
                userId,
                It.Is<CreateCustomRewardsRequest>(r => r.Title == "New Reward")))
                .ReturnsAsync(new List<HelixCustomReward> { createdReward });

            var result = await _service.CreateCustomRewardAsync(userId, request);

            Assert.NotNull(result);
            Assert.Equal("new_r", result.Id);
            Assert.Equal("New Reward", result.Title);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldThrowException_WhenUserNotFound()
        {
            // Arrange
            var userId = "unknown";
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync((User?)null);

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _service.GetCustomRewardsAsync(userId));
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldThrowException_WhenTokenRefreshFails()
        {
            // Arrange
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "expired_token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddMinutes(-1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);
            _mockAuthService.Setup(x => x.RefreshTokenAsync("refresh_token")).ReturnsAsync((TwitchTokenResponse?)null);

            // Act & Assert
            await Assert.ThrowsAsync<ReauthRequiredException>(() => _service.GetCustomRewardsAsync(userId));
        }

        [Fact]
        public async Task CreateCustomRewardAsync_ShouldThrowException_WhenNoRewardsCreated()
        {
            // Arrange
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "access_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var request = new CreateRewardRequest { Title = "Test", Cost = 100 };

            _mockHelixWrapper.Setup(x => x.CreateCustomRewardAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CreateCustomRewardsRequest>()))
                .ReturnsAsync(new List<HelixCustomReward>()); // Empty response

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _service.CreateCustomRewardAsync(userId, request));
        }

        [Fact]
        public async Task DeleteCustomRewardAsync_ShouldCallWrapper()
        {
            // Arrange
            var userId = "12345";
            var rewardId = "reward123";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "access_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // Act
            await _service.DeleteCustomRewardAsync(userId, rewardId);

            // Assert
            _mockHelixWrapper.Verify(x => x.DeleteCustomRewardAsync("test_client_id", "access_token", userId, rewardId), Times.Once);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldRetry_When401()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "bad_token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // First call fails with 401
            _mockHelixWrapper.SetupSequence(x => x.GetCustomRewardsAsync("test_client_id", It.IsAny<string>(), userId))
                .ThrowsAsync(new Exception("401 Unauthorized"))
                .ReturnsAsync(new List<HelixCustomReward>());

            var newToken = new TwitchTokenResponse
            {
                AccessToken = "new_token",
                RefreshToken = "new_refresh",
                ExpiresIn = 3600
            };
            _mockAuthService.Setup(x => x.RefreshTokenAsync("refresh_token")).ReturnsAsync(newToken);

            await _service.GetCustomRewardsAsync(userId);

            _mockAuthService.Verify(x => x.RefreshTokenAsync("refresh_token"), Times.Once);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.AccessToken == "new_token")), Times.Once);
            // Verify GetCustomRewardsAsync was called twice (once with bad token, once with new token)
            _mockHelixWrapper.Verify(x => x.GetCustomRewardsAsync("test_client_id", "bad_token", userId), Times.Once);
            _mockHelixWrapper.Verify(x => x.GetCustomRewardsAsync("test_client_id", "new_token", userId), Times.Once);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldThrow_WhenOtherException()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            _mockHelixWrapper.Setup(x => x.GetCustomRewardsAsync("test_client_id", "token", userId))
                .ThrowsAsync(new Exception("500 Internal Server Error"));

            await Assert.ThrowsAsync<Exception>(() => _service.GetCustomRewardsAsync(userId));

            _mockAuthService.Verify(x => x.RefreshTokenAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldNotRetry_When401AfterRefresh()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "old_token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddMinutes(-1) // Expired, will trigger proactive refresh
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var newToken = new TwitchTokenResponse
            {
                AccessToken = "new_token",
                RefreshToken = "new_refresh",
                ExpiresIn = 3600
            };
            _mockAuthService.Setup(x => x.RefreshTokenAsync("refresh_token")).ReturnsAsync(newToken);

            // Call fails with 401 even with new token
            _mockHelixWrapper.Setup(x => x.GetCustomRewardsAsync("test_client_id", "new_token", userId))
                .ThrowsAsync(new Exception("401 Unauthorized"));

            // Should throw and NOT retry (no second refresh)
            await Assert.ThrowsAsync<ReauthRequiredException>(() => _service.GetCustomRewardsAsync(userId));

            _mockAuthService.Verify(x => x.RefreshTokenAsync("refresh_token"), Times.Once); // Only the proactive refresh
            _mockAuthService.Verify(x => x.RefreshTokenAsync("new_refresh"), Times.Never); // No second refresh
        }

        [Fact]
        public async Task DeleteCustomRewardAsync_ShouldRetry_When401()
        {
            var userId = "12345";
            var rewardId = "reward123";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "bad_token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // First call fails with 401
            _mockHelixWrapper.SetupSequence(x => x.DeleteCustomRewardAsync("test_client_id", It.IsAny<string>(), userId, rewardId))
                .ThrowsAsync(new Exception("401 Unauthorized"))
                .Returns(Task.CompletedTask);

            var newToken = new TwitchTokenResponse
            {
                AccessToken = "new_token",
                RefreshToken = "new_refresh",
                ExpiresIn = 3600
            };
            _mockAuthService.Setup(x => x.RefreshTokenAsync("refresh_token")).ReturnsAsync(newToken);

            await _service.DeleteCustomRewardAsync(userId, rewardId);

            _mockAuthService.Verify(x => x.RefreshTokenAsync("refresh_token"), Times.Once);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.AccessToken == "new_token")), Times.Once);
            _mockHelixWrapper.Verify(x => x.DeleteCustomRewardAsync("test_client_id", "bad_token", userId, rewardId), Times.Once);
            _mockHelixWrapper.Verify(x => x.DeleteCustomRewardAsync("test_client_id", "new_token", userId, rewardId), Times.Once);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldThrow_WhenRefreshTokenEmpty()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "bad_token",
                RefreshToken = "", // Empty refresh token
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            _mockHelixWrapper.Setup(x => x.GetCustomRewardsAsync("test_client_id", "bad_token", userId))
                .ThrowsAsync(new Exception("401 Unauthorized"));

            await Assert.ThrowsAsync<ReauthRequiredException>(() => _service.GetCustomRewardsAsync(userId));

            _mockAuthService.Verify(x => x.RefreshTokenAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldThrow_WhenReactiveRefreshFails()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "bad_token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // First call fails with 401
            _mockHelixWrapper.Setup(x => x.GetCustomRewardsAsync("test_client_id", "bad_token", userId))
                .ThrowsAsync(new Exception("401 Unauthorized"));

            // Refresh fails
            _mockAuthService.Setup(x => x.RefreshTokenAsync("refresh_token")).ReturnsAsync((TwitchTokenResponse?)null);

            await Assert.ThrowsAsync<ReauthRequiredException>(() => _service.GetCustomRewardsAsync(userId));

            _mockAuthService.Verify(x => x.RefreshTokenAsync("refresh_token"), Times.Once);
            // Should not retry with original token or new token
            _mockHelixWrapper.Verify(x => x.GetCustomRewardsAsync("test_client_id", It.IsAny<string>(), userId), Times.Once);
        }

        [Fact]
        public async Task GetCustomRewardsAsync_ShouldRetry_WhenHttpRequestException401()
        {
            var userId = "12345";
            var user = new User
            {
                TwitchUserId = userId,
                AccessToken = "bad_token",
                RefreshToken = "refresh_token",
                TokenExpiry = DateTimeOffset.UtcNow.AddHours(1)
            };

            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            // First call fails with HttpRequestException 401
            var httpEx = new HttpRequestException("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized);
            _mockHelixWrapper.SetupSequence(x => x.GetCustomRewardsAsync("test_client_id", It.IsAny<string>(), userId))
                .ThrowsAsync(httpEx)
                .ReturnsAsync(new List<HelixCustomReward>());

            var newToken = new TwitchTokenResponse
            {
                AccessToken = "new_token",
                RefreshToken = "new_refresh",
                ExpiresIn = 3600
            };
            _mockAuthService.Setup(x => x.RefreshTokenAsync("refresh_token")).ReturnsAsync(newToken);

            await _service.GetCustomRewardsAsync(userId);

            _mockAuthService.Verify(x => x.RefreshTokenAsync("refresh_token"), Times.Once);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.AccessToken == "new_token")), Times.Once);
            _mockHelixWrapper.Verify(x => x.GetCustomRewardsAsync("test_client_id", "bad_token", userId), Times.Once);
            _mockHelixWrapper.Verify(x => x.GetCustomRewardsAsync("test_client_id", "new_token", userId), Times.Once);
        }
    }
}
