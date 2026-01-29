namespace OmniForge.Infrastructure.Configuration
{
    public class TwitchSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string BotRedirectUri { get; set; } = string.Empty;
        public string BotUsername { get; set; } = string.Empty;
        public string BotAccessToken { get; set; } = string.Empty;
        public string BotRefreshToken { get; set; } = string.Empty;

        // Diagnostics
        // When enabled, logs incoming channel.chat.message details (safe, sanitized).
        public bool LogChatMessages { get; set; } = false;

        // When enabled, logs the raw EventSub "event" JSON for channel.chat.message at Debug level.
        public bool LogChatMessagePayload { get; set; } = false;
    }
}
