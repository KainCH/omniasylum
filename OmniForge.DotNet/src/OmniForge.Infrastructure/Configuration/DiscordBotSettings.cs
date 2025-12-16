namespace OmniForge.Infrastructure.Configuration
{
    public class DiscordBotSettings
    {
        // Store in Key Vault / env var. Never log this.
        public string BotToken { get; set; } = string.Empty;

        // Discord Application ID (a.k.a. Client ID) for building the bot invite link.
        // Not a secret.
        public string ApplicationId { get; set; } = string.Empty;

        // OAuth2 permissions integer used in the invite URL.
        // Not a secret.
        public string InvitePermissions { get; set; } = string.Empty;

        // Discord REST API base URL.
        public string ApiBaseUrl { get; set; } = "https://discord.com/api/v10";
    }
}
