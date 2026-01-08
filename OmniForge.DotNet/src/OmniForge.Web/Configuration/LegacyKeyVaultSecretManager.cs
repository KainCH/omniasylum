using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace OmniForge.Web.Configuration
{
    public class LegacyKeyVaultSecretManager : KeyVaultSecretManager
    {
        public override string GetKey(KeyVaultSecret secret)
        {
            var secretName = secret.Name;
            var normalizedName = secretName.ToUpperInvariant();

            return normalizedName switch
            {
                "TWITCH-CLIENT-ID" => "Twitch:ClientId",
                "TWITCH-CLIENT-SECRET" => "Twitch:ClientSecret",
                // Bot login name (used for moderator eligibility checks + bot auth safety checks)
                "TWITCHBOT-UID" => "Twitch:BotUsername",
                "JWT-SECRET" => "Jwt:Secret",

                // Discord bot token (used for Discord notifications)
                "OMNIFORGE-BOT-KEY" => "DiscordBot:BotToken",
                _ => base.GetKey(secret)
            };
        }
    }
}
