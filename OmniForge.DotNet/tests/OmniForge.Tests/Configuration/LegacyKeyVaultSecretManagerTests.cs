using Azure.Security.KeyVault.Secrets;
using System;
using OmniForge.Web.Configuration;
using Xunit;

namespace OmniForge.Tests.Configuration
{
    public class LegacyKeyVaultSecretManagerTests
    {
        [Theory]
        [InlineData("TWITCH-CLIENT-ID", "Twitch:ClientId")]
        [InlineData("twitch-client-secret", "Twitch:ClientSecret")]
        [InlineData("TwitchBot-Uid", "Twitch:BotUsername")]
        [InlineData("JWT-SECRET", "Jwt:Secret")]
        [InlineData("OMNIFORGE-BOT-KEY", "DiscordBot:BotToken")]
        [InlineData("GITHUB-ISSUES-TOKEN", "GitHub:IssuesToken")]
        public void GetKey_ShouldMapKnownSecrets(string name, string expectedKey)
        {
            var manager = new LegacyKeyVaultSecretManager();
            var secret = new KeyVaultSecret(name, "value");

            var key = manager.GetKey(secret);

            Assert.Equal(expectedKey, key);
        }

        [Fact]
        public void GetKey_ShouldFallbackToDefault_ForUnknownSecret()
        {
            var manager = new LegacyKeyVaultSecretManager();
            var secret = new KeyVaultSecret("some-random-secret", "value");

            var key = manager.GetKey(secret);

            // Base behavior is to map '--' to ':' and preserve name; just assert it includes the name.
            Assert.Contains("some-random-secret", key, StringComparison.OrdinalIgnoreCase);
        }
    }
}
