using System.Threading.Tasks;

using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ITwitchAuthService
    {
        string GetAuthorizationUrl(string redirectUri);
        Task<TwitchTokenResponse?> ExchangeCodeForTokenAsync(string code, string redirectUri);
        Task<TwitchTokenResponse?> RefreshTokenAsync(string refreshToken);
        Task<string?> GetAppAccessTokenAsync(IReadOnlyCollection<string>? scopes = null);
        Task<TwitchUserInfo?> GetUserInfoAsync(string accessToken, string clientId);
        Task<string?> GetOidcKeysAsync();
        Task<IReadOnlyList<string>> GetTokenScopesAsync(string accessToken);
        Task<bool> HasScopesAsync(string accessToken, IEnumerable<string> requiredScopes);
        Task<TokenValidationResult?> ValidateTokenAsync(string accessToken);
    }

    public class TwitchTokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string IdToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public string TokenType { get; set; } = string.Empty;
    }

    public class TwitchUserInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ProfileImageUrl { get; set; } = string.Empty;
    }
}
