using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using OmniForge.Core.Utilities;
using OmniForge.Core.Exceptions;

namespace OmniForge.Web.Controllers
{
    [Route("auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ITwitchAuthService _twitchAuthService;
        private readonly IUserRepository _userRepository;
        private readonly IBotCredentialRepository _botCredentialRepository;
        private readonly IJwtService _jwtService;
        private readonly TwitchSettings _twitchSettings;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            ITwitchAuthService twitchAuthService,
            IUserRepository userRepository,
            IBotCredentialRepository botCredentialRepository,
            IJwtService jwtService,
            IOptions<TwitchSettings> twitchSettings,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _twitchAuthService = twitchAuthService;
            _userRepository = userRepository;
            _botCredentialRepository = botCredentialRepository;
            _jwtService = jwtService;
            _twitchSettings = twitchSettings.Value;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("twitch")]
        public IActionResult Login()
        {
            var redirectUri = GetRedirectUri();
            var url = _twitchAuthService.GetAuthorizationUrl(redirectUri);
            return Redirect(url);
        }

        [HttpGet("twitch/bot")]
        [Authorize(Roles = "admin", AuthenticationSchemes = "Bearer,Cookies")]
        public IActionResult BotLogin()
        {
            var redirectUri = GetBotRedirectUri();

            // Request the same scopes as a normal OmniForge user login, plus the IRC scopes used by TwitchLib.Client.
            // This is admin-only and intended for the dedicated Forge bot account.
            var scopes = new List<string>
            {
                "openid",
                "user:read:email",

                // EventSub/Helix chat
                "user:read:chat",
                "user:write:chat",
                "user:bot",

                // Whispers (optional - for DM functionality)
                "user:manage:whispers",

                // Channel features
                "channel:read:subscriptions",
                "channel:read:redemptions",
                "channel:manage:polls",

                // Moderation & followers
                "moderator:read:followers",
                "moderation:read",
                "moderator:read:automod_settings",
                "moderator:manage:automod_settings",

                // Moderation actions (requires the bot to be a mod in the target channel)
                "moderator:manage:banned_users",
                "moderator:manage:chat_messages",

                // Bits & clips
                "bits:read",
                "clips:edit",

                // IRC chat (TwitchLib Client)
                "chat:read",
                "chat:edit"
            };

            var scopeString = string.Join(" ", scopes);
            var encodedScopes = System.Net.WebUtility.UrlEncode(scopeString);
            var encodedRedirect = System.Net.WebUtility.UrlEncode(redirectUri);
            var encodedClientId = System.Net.WebUtility.UrlEncode(_twitchSettings.ClientId);

            var url = $"https://id.twitch.tv/oauth2/authorize?client_id={encodedClientId}&redirect_uri={encodedRedirect}&response_type=code&scope={encodedScopes}&force_verify=true";
            return Redirect(url);
        }

        [HttpGet("twitch/bot/callback")]
        [Authorize(Roles = "admin", AuthenticationSchemes = "Bearer,Cookies")]
        public async Task<IActionResult> BotCallback([FromQuery] string code)
        {
            return await HandleBotCallbackAsync(code);
        }

        private async Task<IActionResult> HandleBotCallbackAsync(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return BadRequest("No authorization code provided");
            }

            var redirectUri = GetBotRedirectUri();
            var tokenResponse = await _twitchAuthService.ExchangeCodeForTokenAsync(code, redirectUri);

            if (tokenResponse == null)
            {
                return BadRequest("Failed to exchange authorization code");
            }

            var userInfo = await _twitchAuthService.GetUserInfoAsync(tokenResponse.AccessToken, _twitchSettings.ClientId);
            if (userInfo == null)
            {
                return BadRequest("Failed to get bot user info from Twitch");
            }

            // Optional safety check: if BotUsername is configured, enforce it.
            if (!string.IsNullOrWhiteSpace(_twitchSettings.BotUsername)
                && !string.Equals(_twitchSettings.BotUsername, userInfo.Login, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "⚠️ Bot OAuth completed for {Login}, but configured BotUsername is {Configured}. Rejecting.",
                    LogSanitizer.Sanitize(userInfo.Login),
                    LogSanitizer.Sanitize(_twitchSettings.BotUsername));

                return BadRequest($"Authorized account '{userInfo.Login}' does not match configured bot username.");
            }

            await _botCredentialRepository.SaveAsync(new BotCredentials
            {
                Username = userInfo.Login,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
            });

            _logger.LogInformation("✅ Forge bot authorized as {Login}", LogSanitizer.Sanitize(userInfo.Login));
            return Redirect("/portal");
        }

        [HttpGet("twitch/callback")]
        public async Task<IActionResult> Callback([FromQuery] string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return BadRequest("No authorization code provided");
            }

            var redirectUri = GetRedirectUri();
            var tokenResponse = await _twitchAuthService.ExchangeCodeForTokenAsync(code, redirectUri);

            if (tokenResponse == null)
            {
                return BadRequest("Failed to exchange authorization code");
            }

            TwitchUserInfo? userInfo = null;

            // Try to parse ID Token if available (OIDC)
            // OIDC validation is preferred because:
            // 1. It's faster (no additional API call to Twitch)
            // 2. It provides cryptographically verified user identity
            // 3. It follows OAuth 2.0 / OpenID Connect best practices
            if (!string.IsNullOrEmpty(tokenResponse.IdToken))
            {
                try
                {
                    var handler = new JwtSecurityTokenHandler();
                    handler.InboundClaimTypeMap.Clear(); // Ensure claims are not mapped to .NET types
                    if (handler.CanReadToken(tokenResponse.IdToken))
                    {
                        // Fetch OIDC keys for validation
                        var jwksJson = await _twitchAuthService.GetOidcKeysAsync();
                        if (string.IsNullOrEmpty(jwksJson))
                        {
                            throw new InvalidOperationException("Failed to fetch OIDC keys");
                        }

                        var jwks = new JsonWebKeySet(jwksJson);
                        var validationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = "https://id.twitch.tv/oauth2",
                            ValidateAudience = true,
                            ValidAudience = _twitchSettings.ClientId,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKeys = jwks.Keys,
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.FromMinutes(5) // Allow some clock skew
                        };

                        var principal = handler.ValidateToken(tokenResponse.IdToken, validationParameters, out var validatedToken);

                        var sub = principal.FindFirst(c => c.Type == "sub")?.Value;
                        var preferredUsername = principal.FindFirst(c => c.Type == "preferred_username")?.Value;

                        if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(preferredUsername))
                        {
                            userInfo = new TwitchUserInfo
                            {
                                Id = sub,
                                Login = preferredUsername, // OIDC 'preferred_username' is the Twitch login name (lowercase, unique)
                                // Use the 'name' claim for display name if present, otherwise fall back to login name
                                DisplayName = principal.FindFirst(c => c.Type == "name")?.Value ?? preferredUsername,
                                Email = principal.FindFirst(c => c.Type == "email")?.Value ?? "",
                                ProfileImageUrl = principal.FindFirst(c => c.Type == "picture")?.Value ?? ""
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate/parse ID Token. Falling back to Helix API.");
                }
            }

            // Fallback to Helix API if OIDC failed or missing
            if (userInfo == null)
            {
                userInfo = await _twitchAuthService.GetUserInfoAsync(tokenResponse.AccessToken, _twitchSettings.ClientId);
            }

            if (userInfo == null)
            {
                return BadRequest("Failed to get user info from Twitch");
            }

            // Check if user exists
            var existingUser = await _userRepository.GetUserAsync(userInfo.Id);

            var user = existingUser ?? new User
            {
                TwitchUserId = userInfo.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                Role = "streamer", // Default role
                IsActive = true,
                Features = new FeatureFlags()
            };

            // Update user info
            user.Username = userInfo.Login;
            user.DisplayName = userInfo.DisplayName;
            user.Email = userInfo.Email;
            user.ProfileImageUrl = userInfo.ProfileImageUrl;
            user.AccessToken = tokenResponse.AccessToken;
            user.RefreshToken = tokenResponse.RefreshToken;
            user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            user.LastLogin = DateTimeOffset.UtcNow;

            // Admin check (hardcoded in legacy) - Restored to ensure admin access is preserved/restored
            if (user.Username.ToLower() == "riress")
            {
                user.Role = "admin";
            }

            await _userRepository.SaveUserAsync(user);

            // Create ClaimsPrincipal for Cookie Auth
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.TwitchUserId),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("userId", user.TwitchUserId)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTime.UtcNow.AddDays(30)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // Redirect to Portal
            return Redirect("/portal");
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout([FromQuery] string? returnUrl = null, [FromQuery] int reauth = 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Safety: only allow local return URLs.
            if (!string.IsNullOrEmpty(returnUrl) && !Url.IsLocalUrl(returnUrl))
            {
                returnUrl = null;
            }

            if (reauth == 1)
            {
                // Force full OAuth flow.
                return Redirect("/auth/twitch");
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return Redirect("/");
        }

        [HttpPost("refresh")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> Refresh()
        {
            var userIdClaim = User.FindFirst("userId");
            if (userIdClaim == null)
            {
                return Unauthorized("Invalid token claims");
            }

            var user = await _userRepository.GetUserAsync(userIdClaim.Value);
            if (user == null)
            {
                return NotFound("User not found");
            }

            if (!user.IsActive)
            {
                return Unauthorized("User account is inactive");
            }

            // Check if Twitch token needs refresh (e.g. expires in < 1 hour)
            // Note: TokenExpiry is DateTimeOffset
            var timeUntilExpiry = user.TokenExpiry - DateTimeOffset.UtcNow;

            if (timeUntilExpiry.TotalMinutes > 60)
            {
                return Ok(new { message = "Token still valid", expiresAt = user.TokenExpiry });
            }

            // Refresh Twitch Token
            var newTokens = await _twitchAuthService.RefreshTokenAsync(user.RefreshToken);

            if (newTokens == null)
            {
                // If refresh fails, user needs to re-login
                return Unauthorized(new { error = "Authentication expired", requireReauth = true, authUrl = "/auth/twitch" });
            }

            // Update user with new tokens
            user.AccessToken = newTokens.AccessToken;
            user.RefreshToken = newTokens.RefreshToken;
            user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(newTokens.ExpiresIn);

            await _userRepository.SaveUserAsync(user);

            // Generate new JWT
            var newJwtToken = _jwtService.GenerateToken(user);

            Response.Headers.Append("X-New-Token", newJwtToken);
            Response.Headers.Append("X-Token-Refreshed", "true");

            return Ok(new { message = "Token refreshed", expiresAt = user.TokenExpiry });
        }

        private string GetRedirectUri()
        {
            // CWE-247, CWE-350, CWE-807 Fix:
            // Do not rely on Request.Host or Request.Scheme which are user-controlled.
            // Require explicit configuration of the redirect URI.
            var redirectUri = _configuration["Twitch:RedirectUri"];

            if (string.IsNullOrEmpty(redirectUri))
            {
                _logger.LogCritical("Twitch:RedirectUri is not configured. Authentication cannot proceed.");
                throw new ConfigurationException("Missing required configuration: Twitch:RedirectUri");
            }

            // Allow indirection via Key Vault secret/config key name.
            // Example: Twitch:RedirectUri = "dev-callback" and Key Vault secret "dev-callback" contains the full URL.
            if (Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsed)
                && (string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            {
                return redirectUri;
            }

            var resolved = _configuration[redirectUri];
            if (!string.IsNullOrEmpty(resolved))
            {
                return resolved;
            }

            _logger.LogCritical(
                "Twitch:RedirectUri was set to '{RedirectUri}', but it was not a valid URL and could not be resolved from configuration.",
                LogSanitizer.Sanitize(redirectUri));
            throw new ConfigurationException(
                "Invalid configuration for Twitch:RedirectUri. Provide an absolute http(s) URL or a configuration key whose value is an absolute http(s) URL.");
        }

        private string GetBotRedirectUri()
        {
            // Prefer explicit config.
            var botRedirect = _configuration["Twitch:BotRedirectUri"];
            if (!string.IsNullOrEmpty(botRedirect))
            {
                // Allow indirection via Key Vault secret name.
                // Example: Twitch:BotRedirectUri = "Dev-bot-callback" and Key Vault secret "Dev-bot-callback" contains the full URL.
                if (Uri.TryCreate(botRedirect, UriKind.Absolute, out var botRedirectUri)
                    && (string.Equals(botRedirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(botRedirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
                {
                    return botRedirect;
                }

                var resolved = _configuration[botRedirect];
                if (!string.IsNullOrEmpty(resolved))
                {
                    return resolved;
                }

                _logger.LogCritical(
                    "Twitch:BotRedirectUri was set to '{BotRedirect}', but it was not a valid URL and could not be resolved from configuration.",
                    LogSanitizer.Sanitize(botRedirect));
                throw new ConfigurationException(
                    "Invalid configuration for Twitch:BotRedirectUri. Provide an absolute http(s) URL or a configuration key whose value is an absolute http(s) URL.");
            }

            // Safe fallback: derive from the configured user redirect (not from Request.Host).
            var redirectUri = GetRedirectUri();
            if (redirectUri.Contains("/auth/twitch/callback", StringComparison.OrdinalIgnoreCase))
            {
                return redirectUri.Replace("/auth/twitch/callback", "/auth/twitch/bot/callback", StringComparison.OrdinalIgnoreCase);
            }

            _logger.LogCritical("Twitch:BotRedirectUri is not configured and could not be derived.");
            throw new ConfigurationException("Missing required configuration: Twitch:BotRedirectUri");
        }
    }
}
