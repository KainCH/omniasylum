using Microsoft.AspNetCore.Http;
using OmniForge.Core.Interfaces;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace OmniForge.Web.Middleware
{
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;

        public AuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
        {
            // Skip middleware for Blazor SignalR requests, framework files, and WebSocket connections
            if (context.Request.Path.StartsWithSegments("/_blazor") ||
                context.Request.Path.StartsWithSegments("/_framework") ||
                context.Request.Path.StartsWithSegments("/overlayHub") ||
                context.Request.Path.StartsWithSegments("/ws/overlay") ||
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
                            Console.WriteLine($"[AuthMiddleware] User {userIdClaim.Value} not found in database.");
                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsJsonAsync(new { error = "User not found" });
                            return;
                        }

                        if (!user.IsActive)
                        {
                            Console.WriteLine($"[AuthMiddleware] User {userIdClaim.Value} is inactive.");
                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsJsonAsync(new { error = "Account deactivated" });
                            return;
                        }

                        // Attach user to context items for easy access
                        context.Items["User"] = user;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AuthMiddleware] Error retrieving user: {ex.Message}");
                        // Don't block request on DB error, but log it
                    }
                }
            }

            await _next(context);
        }
    }
}
