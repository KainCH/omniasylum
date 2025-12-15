namespace OmniForge.Infrastructure.Configuration
{
    public class DiscordBotSettings
    {
        // Store in Key Vault / env var. Never log this.
        public string BotToken { get; set; } = string.Empty;

        // Discord REST API base URL.
        public string ApiBaseUrl { get; set; } = "https://discord.com/api/v10";
    }
}
