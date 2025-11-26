namespace OmniForge.Infrastructure.Configuration
{
    public class JwtSettings
    {
        public string Secret { get; set; } = string.Empty;
        public int ExpiryDays { get; set; } = 30;
        public string Issuer { get; set; } = "OmniForge";
        public string Audience { get; set; } = "OmniForgeClient";
    }
}
