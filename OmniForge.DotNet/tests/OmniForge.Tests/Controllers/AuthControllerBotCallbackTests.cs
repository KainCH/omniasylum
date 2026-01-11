using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Web.Controllers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Controllers
{
    public class AuthControllerBotCallbackTests
    {
        private static AuthController CreateController(
            IConfiguration configuration,
            TwitchSettings twitchSettings,
            Mock<ITwitchAuthService> twitchAuthServiceMock,
            Mock<IBotCredentialRepository> botCredentialRepositoryMock,
            Mock<ILogger<AuthController>> loggerMock)
        {
            var userRepositoryMock = new Mock<IUserRepository>(MockBehavior.Loose);
            var jwtServiceMock = new Mock<IJwtService>(MockBehavior.Loose);

            return new AuthController(
                twitchAuthServiceMock.Object,
                userRepositoryMock.Object,
                botCredentialRepositoryMock.Object,
                jwtServiceMock.Object,
                Options.Create(twitchSettings),
                configuration,
                loggerMock.Object);
        }

        [Fact]
        public async Task BotCallback_WhenCodeMissing_ShouldReturnBadRequest()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

            var twitchAuthServiceMock = new Mock<ITwitchAuthService>(MockBehavior.Strict);
            var botCredentialRepositoryMock = new Mock<IBotCredentialRepository>(MockBehavior.Strict);
            var loggerMock = new Mock<ILogger<AuthController>>(MockBehavior.Loose);

            var controller = CreateController(
                config,
                new TwitchSettings { ClientId = "cid", BotUsername = "bot" },
                twitchAuthServiceMock,
                botCredentialRepositoryMock,
                loggerMock);

            var result = await controller.BotCallback("");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("No authorization code provided", badRequest.Value);
        }

        [Fact]
        public async Task BotCallback_WhenTokenExchangeFails_ShouldReturnBadRequest()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Twitch:BotRedirectUri"] = "https://example.test/auth/twitch/bot/callback"
            }).Build();

            var twitchAuthServiceMock = new Mock<ITwitchAuthService>(MockBehavior.Strict);
            twitchAuthServiceMock
                .Setup(s => s.ExchangeCodeForTokenAsync("code", "https://example.test/auth/twitch/bot/callback"))
                .ReturnsAsync((TwitchTokenResponse?)null);

            var botCredentialRepositoryMock = new Mock<IBotCredentialRepository>(MockBehavior.Strict);
            var loggerMock = new Mock<ILogger<AuthController>>(MockBehavior.Loose);

            var controller = CreateController(
                config,
                new TwitchSettings { ClientId = "cid", BotUsername = "bot" },
                twitchAuthServiceMock,
                botCredentialRepositoryMock,
                loggerMock);

            var result = await controller.BotCallback("code");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Failed to exchange authorization code", badRequest.Value);
            twitchAuthServiceMock.VerifyAll();
        }

        [Fact]
        public async Task BotCallback_WhenUserInfoFails_ShouldReturnBadRequest()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Twitch:BotRedirectUri"] = "https://example.test/auth/twitch/bot/callback"
            }).Build();

            var token = new TwitchTokenResponse
            {
                AccessToken = "at",
                RefreshToken = "rt",
                ExpiresIn = 3600
            };

            var twitchAuthServiceMock = new Mock<ITwitchAuthService>(MockBehavior.Strict);
            twitchAuthServiceMock
                .Setup(s => s.ExchangeCodeForTokenAsync("code", "https://example.test/auth/twitch/bot/callback"))
                .ReturnsAsync(token);
            twitchAuthServiceMock
                .Setup(s => s.GetTokenScopesAsync("at"))
                .ReturnsAsync(Array.Empty<string>());
            twitchAuthServiceMock
                .Setup(s => s.GetUserInfoAsync("at", "cid"))
                .ReturnsAsync((TwitchUserInfo?)null);

            var botCredentialRepositoryMock = new Mock<IBotCredentialRepository>(MockBehavior.Strict);
            var loggerMock = new Mock<ILogger<AuthController>>(MockBehavior.Loose);

            var controller = CreateController(
                config,
                new TwitchSettings { ClientId = "cid", BotUsername = "bot" },
                twitchAuthServiceMock,
                botCredentialRepositoryMock,
                loggerMock);

            var result = await controller.BotCallback("code");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Failed to get bot user info from Twitch", badRequest.Value);
            twitchAuthServiceMock.VerifyAll();
        }

        [Fact]
        public async Task BotCallback_WhenBotUsernameMismatch_ShouldReturnBadRequest()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Twitch:BotRedirectUri"] = "https://example.test/auth/twitch/bot/callback"
            }).Build();

            var token = new TwitchTokenResponse
            {
                AccessToken = "at",
                RefreshToken = "rt",
                ExpiresIn = 3600
            };

            var twitchAuthServiceMock = new Mock<ITwitchAuthService>(MockBehavior.Strict);
            twitchAuthServiceMock
                .Setup(s => s.ExchangeCodeForTokenAsync("code", "https://example.test/auth/twitch/bot/callback"))
                .ReturnsAsync(token);
            twitchAuthServiceMock
                .Setup(s => s.GetTokenScopesAsync("at"))
                .ReturnsAsync(new[] { "openid" });
            twitchAuthServiceMock
                .Setup(s => s.GetUserInfoAsync("at", "cid"))
                .ReturnsAsync(new TwitchUserInfo { Login = "someother" });

            var botCredentialRepositoryMock = new Mock<IBotCredentialRepository>(MockBehavior.Strict);
            var loggerMock = new Mock<ILogger<AuthController>>(MockBehavior.Loose);

            var controller = CreateController(
                config,
                new TwitchSettings { ClientId = "cid", BotUsername = "forgebot" },
                twitchAuthServiceMock,
                botCredentialRepositoryMock,
                loggerMock);

            var result = await controller.BotCallback("code");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("does not match configured bot username", badRequest.Value?.ToString());
            twitchAuthServiceMock.VerifyAll();
        }

        [Fact]
        public async Task BotCallback_OnSuccess_ShouldSaveCredentials_AndRedirect()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Twitch:BotRedirectUri"] = "https://example.test/auth/twitch/bot/callback"
            }).Build();

            var token = new TwitchTokenResponse
            {
                AccessToken = "at",
                RefreshToken = "rt",
                ExpiresIn = 123
            };

            var twitchAuthServiceMock = new Mock<ITwitchAuthService>(MockBehavior.Strict);
            twitchAuthServiceMock
                .Setup(s => s.ExchangeCodeForTokenAsync("code", "https://example.test/auth/twitch/bot/callback"))
                .ReturnsAsync(token);
            twitchAuthServiceMock
                .Setup(s => s.GetTokenScopesAsync("at"))
                .ReturnsAsync(new[] { "openid" });
            twitchAuthServiceMock
                .Setup(s => s.GetUserInfoAsync("at", "cid"))
                .ReturnsAsync(new TwitchUserInfo { Login = "forgebot" });

            var saved = (Username: (string?)null, AccessToken: (string?)null, RefreshToken: (string?)null, TokenExpiry: (DateTimeOffset?)null);
            var botCredentialRepositoryMock = new Mock<IBotCredentialRepository>(MockBehavior.Strict);
            botCredentialRepositoryMock
                .Setup(r => r.SaveAsync(It.IsAny<OmniForge.Core.Entities.BotCredentials>()))
                .Callback<OmniForge.Core.Entities.BotCredentials>(c => saved = (c.Username, c.AccessToken, c.RefreshToken, c.TokenExpiry))
                .Returns(Task.CompletedTask);

            var loggerMock = new Mock<ILogger<AuthController>>(MockBehavior.Loose);

            var controller = CreateController(
                config,
                new TwitchSettings { ClientId = "cid", BotUsername = "forgebot" },
                twitchAuthServiceMock,
                botCredentialRepositoryMock,
                loggerMock);

            var before = DateTimeOffset.UtcNow;
            var result = await controller.BotCallback("code");
            var after = DateTimeOffset.UtcNow;

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/portal", redirect.Url);

            Assert.Equal("forgebot", saved.Username);
            Assert.Equal("at", saved.AccessToken);
            Assert.Equal("rt", saved.RefreshToken);
            Assert.NotNull(saved.TokenExpiry);
            Assert.True(saved.TokenExpiry >= before);
            Assert.True(saved.TokenExpiry <= after.AddSeconds(123 + 5));

            botCredentialRepositoryMock.VerifyAll();
            twitchAuthServiceMock.VerifyAll();
        }
    }
}
