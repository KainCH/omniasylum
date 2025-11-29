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
using System.Security.Claims;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace OmniForge.Web.Controllers
{
    [Route("auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ITwitchAuthService _twitchAuthService;
        private readonly IUserRepository _userRepository;
        private readonly IJwtService _jwtService;
        private readonly TwitchSettings _twitchSettings;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            ITwitchAuthService twitchAuthService,
            IUserRepository userRepository,
            IJwtService jwtService,
            IOptions<TwitchSettings> twitchSettings,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _twitchAuthService = twitchAuthService;
            _userRepository = userRepository;
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
            if (!string.IsNullOrEmpty(tokenResponse.IdToken))
            {
                try
                {
                    var handler = new JwtSecurityTokenHandler();
                    if (handler.CanReadToken(tokenResponse.IdToken))
                    {
                        var jwt = handler.ReadJwtToken(tokenResponse.IdToken);
                        var sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                        var preferredUsername = jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;

                        if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(preferredUsername))
                        {
                            userInfo = new TwitchUserInfo
                            {
                                Id = sub,
                                Login = preferredUsername, // Note: OIDC 'preferred_username' is usually the display name, but often matches login
                                DisplayName = preferredUsername,
                                Email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? "",
                                ProfileImageUrl = jwt.Claims.FirstOrDefault(c => c.Type == "picture")?.Value ?? ""
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse ID Token");
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
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/");
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromHeader(Name = "Authorization")] string authHeader)
        {
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Unauthorized("No token provided");
            }

            var token = authHeader.Substring(7);
            // In a real scenario, we should validate the token here using the same logic as the middleware.
            // However, since we are manually handling the refresh, we might want to decode it even if expired?
            // The legacy code verifies it.

            // TODO: We need a way to validate/decode the token manually here or rely on the [Authorize] attribute
            // but allow expired tokens? The legacy code manually verifies.

            // For now, let's assume the client sends a valid (or recently expired) token.
            // We need a method in JwtService to Validate/Decode token.

            // Let's skip the manual validation for a second and assume we can get the userId from claims if we were authorized.
            // But if the token is expired, [Authorize] will fail.
            // The legacy code allows refreshing an expired token if the underlying Twitch token is valid.

            // So we need to manually decode the token without validating expiry.

            var principal = _jwtService.GetPrincipalFromExpiredToken(token);
            if (principal == null)
            {
                return Unauthorized("Invalid token");
            }

            // CWE-247, CWE-350, CWE-807 Fix:
            // Ensure the token is not expired. We do not support refreshing with expired tokens
            // as we lack a secure Refresh Token mechanism.
            var expiryClaim = principal.FindFirst("exp");
            if (expiryClaim != null && long.TryParse(expiryClaim.Value, out var expiryUnix))
            {
                var expiryDate = DateTimeOffset.FromUnixTimeSeconds(expiryUnix);
                if (expiryDate < DateTimeOffset.UtcNow)
                {
                    return Unauthorized("Token expired. Please login again.");
                }
            }

            var userIdClaim = principal.FindFirst("userId");
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
                throw new InvalidOperationException("Twitch:RedirectUri is not configured.");
            }

            return redirectUri;
        }
    }
}
