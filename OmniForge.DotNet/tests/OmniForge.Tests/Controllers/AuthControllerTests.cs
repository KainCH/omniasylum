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
    }
}
