using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Exceptions;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Web.Controllers;
using System.Collections.Generic;
using System.Net;
using Xunit;

namespace OmniForge.Tests.Controllers
{
    public class AuthControllerTests
    {
        private static AuthController CreateController(
            IConfiguration configuration,
            TwitchSettings? twitchSettings = null,
            Mock<ITwitchAuthService>? twitchAuthServiceMock = null)
        {
            twitchSettings ??= new TwitchSettings
            {
                ClientId = "test-client-id"
            };

            twitchAuthServiceMock ??= new Mock<ITwitchAuthService>(MockBehavior.Strict);

            var userRepositoryMock = new Mock<IUserRepository>(MockBehavior.Loose);
            var botCredentialRepositoryMock = new Mock<IBotCredentialRepository>(MockBehavior.Loose);
            var jwtServiceMock = new Mock<IJwtService>(MockBehavior.Loose);
            var loggerMock = new Mock<ILogger<AuthController>>(MockBehavior.Loose);

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
        public void Login_UsesConfiguredRedirectUriUrl()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Twitch:RedirectUri"] = "https://example.test/auth/twitch/callback"
                })
                .Build();

            var twitchAuthServiceMock = new Mock<ITwitchAuthService>(MockBehavior.Strict);
            twitchAuthServiceMock
                .Setup(s => s.GetAuthorizationUrl("https://example.test/auth/twitch/callback"))
                .Returns("https://id.twitch.tv/oauth2/authorize?redirect_uri=xyz");

            var controller = CreateController(config, twitchAuthServiceMock: twitchAuthServiceMock);

            var result = controller.Login();

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://id.twitch.tv/oauth2/authorize?redirect_uri=xyz", redirect.Url);
            twitchAuthServiceMock.VerifyAll();
        }

        [Fact]
        public void Login_ResolvesRedirectUriFromConfigKeyIndirection()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Indirection: value is a config key name.
                    ["Twitch:RedirectUri"] = "dev-callback",
                    ["dev-callback"] = "https://example.test/auth/twitch/callback"
                })
                .Build();

            var twitchAuthServiceMock = new Mock<ITwitchAuthService>(MockBehavior.Strict);
            twitchAuthServiceMock
                .Setup(s => s.GetAuthorizationUrl("https://example.test/auth/twitch/callback"))
                .Returns("https://id.twitch.tv/oauth2/authorize?redirect_uri=abc");

            var controller = CreateController(config, twitchAuthServiceMock: twitchAuthServiceMock);

            var result = controller.Login();

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://id.twitch.tv/oauth2/authorize?redirect_uri=abc", redirect.Url);
            twitchAuthServiceMock.VerifyAll();
        }

        [Fact]
        public void Login_ThrowsConfigurationException_WhenRedirectUriMissing()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var controller = CreateController(config, twitchAuthServiceMock: new Mock<ITwitchAuthService>(MockBehavior.Loose));

            Assert.Throws<ConfigurationException>(() => controller.Login());
        }

        [Fact]
        public void BotLogin_UsesExplicitBotRedirectUri_WhenConfiguredAsUrl()
        {
            var botRedirect = "https://example.test/auth/twitch/bot/callback";

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Twitch:BotRedirectUri"] = botRedirect
                })
                .Build();

            var controller = CreateController(config);

            var result = controller.BotLogin();

            var redirect = Assert.IsType<RedirectResult>(result);

            var expectedEncodedRedirect = WebUtility.UrlEncode(botRedirect);
            Assert.Contains($"redirect_uri={expectedEncodedRedirect}", redirect.Url);
            Assert.Contains("client_id=test-client-id", redirect.Url);
            Assert.Contains("scope=", redirect.Url);
            Assert.Contains(WebUtility.UrlEncode("chat:read"), redirect.Url);
            Assert.Contains(WebUtility.UrlEncode("chat:edit"), redirect.Url);
        }

        [Fact]
        public void BotLogin_ResolvesBotRedirectUriFromConfigKeyIndirection()
        {
            var botRedirect = "https://example.test/auth/twitch/bot/callback";

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Twitch:BotRedirectUri"] = "bot-callback",
                    ["bot-callback"] = botRedirect
                })
                .Build();

            var controller = CreateController(config);

            var result = controller.BotLogin();

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Contains($"redirect_uri={WebUtility.UrlEncode(botRedirect)}", redirect.Url);
        }

        [Fact]
        public void BotLogin_DerivesBotRedirectUriFromUserRedirectUri_WhenBotRedirectUriMissing()
        {
            var userRedirect = "https://example.test/auth/twitch/callback";
            var expectedBotRedirect = "https://example.test/auth/twitch/bot/callback";

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Twitch:RedirectUri"] = userRedirect
                })
                .Build();

            var controller = CreateController(config);

            var result = controller.BotLogin();

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Contains($"redirect_uri={WebUtility.UrlEncode(expectedBotRedirect)}", redirect.Url);
        }

        [Fact]
        public void BotLogin_ThrowsConfigurationException_WhenBotRedirectUriIndirectionMissing()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Twitch:BotRedirectUri"] = "missing-key"
                })
                .Build();

            var controller = CreateController(config);

            Assert.Throws<ConfigurationException>(() => controller.BotLogin());
        }

        [Fact]
        public void BotLogin_ThrowsConfigurationException_WhenBotRedirectUriCannotBeDerived()
        {
            // No Twitch:BotRedirectUri, and user redirect doesn't have the expected callback path.
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Twitch:RedirectUri"] = "https://example.test/not-a-callback"
                })
                .Build();

            var controller = CreateController(config);

            Assert.Throws<ConfigurationException>(() => controller.BotLogin());
        }
    }
}
