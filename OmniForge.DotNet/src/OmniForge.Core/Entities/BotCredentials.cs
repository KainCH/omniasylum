using System;

namespace OmniForge.Core.Entities
{
    public class BotCredentials
    {
        public string Username { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset TokenExpiry { get; set; }
    }
}
