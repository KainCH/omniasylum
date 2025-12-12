namespace OmniForge.Core.Entities
{
    public class TokenValidationResult
    {
        public string ClientId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();
        public int ExpiresIn { get; set; }
    }
}
