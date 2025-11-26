namespace OmniForge.Infrastructure.Configuration
{
    public class TwitchSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string BotUsername { get; set; } = string.Empty;
        public string BotAccessToken { get; set; } = string.Empty;
        public string BotRefreshToken { get; set; } = string.Empty;
    }
}
