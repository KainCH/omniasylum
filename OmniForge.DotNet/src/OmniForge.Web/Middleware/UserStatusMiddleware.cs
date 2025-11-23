using Microsoft.AspNetCore.Http;
using OmniForge.Core.Interfaces;
using System.Threading.Tasks;

namespace OmniForge.Web.Middleware
{
    public class UserStatusMiddleware
    {
        private readonly RequestDelegate _next;

        public UserStatusMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
        {
            // Skip middleware for Blazor SignalR requests, framework files, and WebSocket connections
            // This prevents interference with the SignalR handshake and heartbeat
            if (context.Request.Path.StartsWithSegments("/_blazor") ||
                context.Request.Path.StartsWithSegments("/_framework") ||
                context.WebSockets.IsWebSocketRequest)
            {
                await _next(context);
                return;
            }

            try
            {
                if (context.User.Identity?.IsAuthenticated == true)
                {
                    var userIdClaim = context.User.FindFirst("userId");
                    if (userIdClaim != null)
                    {
                        var user = await userRepository.GetUserAsync(userIdClaim.Value);
                        if (user == null || !user.IsActive)
                        {
                            Console.WriteLine($"[UserStatusMiddleware] User {userIdClaim.Value} not found or inactive. Path: {context.Request.Path}");

                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsJsonAsync(new { error = "User not found or inactive" });
                            return;
                        }

                        // Optionally attach user to context items for easy access
                        context.Items["User"] = user;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't crash the request if possible, or return 500
                Console.WriteLine($"Error in UserStatusMiddleware: {ex}");
                // If it's a critical auth check, we might want to fail.
                // But for now, let's allow it to proceed if the DB is down,
                // though the app might fail later.
            }

            await _next(context);
        }
    }
}
