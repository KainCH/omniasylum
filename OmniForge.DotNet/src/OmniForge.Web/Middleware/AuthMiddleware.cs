using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Exceptions;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Net.Mime;
using System.Text.Encodings.Web;

namespace OmniForge.Web.Middleware
{
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuthMiddleware> _logger;

        public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
        {
            // Skip middleware for Blazor SignalR requests, framework files, WebSocket connections, AND the overlay page itself
            // The overlay should always be treated as anonymous to ensure consistent rendering behavior
            if (context.Request.Path.StartsWithSegments("/_blazor") ||
                context.Request.Path.StartsWithSegments("/_framework") ||
                context.Request.Path.StartsWithSegments("/overlayHub") ||
                context.Request.Path.StartsWithSegments("/ws/overlay") ||
                context.Request.Path.StartsWithSegments("/overlay") ||
                context.WebSockets.IsWebSocketRequest)
            {
                await _next(context);
                return;
            }

            // Check if user is authenticated
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = context.User.FindFirst("userId");
                if (userIdClaim != null)
                {
                    try
                    {
                        var user = await userRepository.GetUserAsync(userIdClaim.Value);

                        if (user == null)
                        {
                            _logger.LogWarning("[AuthMiddleware] User {UserId} not found in database.", LogSanitizer.Sanitize(userIdClaim.Value));
                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsJsonAsync(new { error = "User not found" });
                            return;
                        }

                        // Attach user to context items for easy access
                        context.Items["User"] = user;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[AuthMiddleware] Error retrieving user");
                        // Don't block request on DB error, but log it
                    }
                }
            }

            try
            {
                await _next(context);
            }
            catch (ReauthRequiredException ex)
            {
                _logger.LogWarning(ex, "[AuthMiddleware] Reauth required: {Message}", LogSanitizer.Sanitize(ex.Message));

                if (!context.Response.HasStarted)
                {
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    var isApi = context.Request.Path.StartsWithSegments("/api");
                    var acceptsJson = context.Request.Headers.Accept.Any(a => a.Contains("application/json", StringComparison.OrdinalIgnoreCase));

                    if (isApi || acceptsJson)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = MediaTypeNames.Application.Json;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "Authentication expired",
                            requireReauth = true,
                            authUrl = "/auth/twitch",
                            logoutUrl = "/auth/logout?reauth=1"
                        });
                        return;
                    }

                    var returnUrl = context.Request.Path + context.Request.QueryString;
                    var encodedReturnUrl = UrlEncoder.Default.Encode(returnUrl);
                    context.Response.Redirect($"/auth/logout?reauth=1&returnUrl={encodedReturnUrl}");
                    return;
                }

                throw;
            }
        }
    }
}
