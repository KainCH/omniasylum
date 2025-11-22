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
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = context.User.FindFirst("userId");
                if (userIdClaim != null)
                {
                    var user = await userRepository.GetUserAsync(userIdClaim.Value);
                    if (user == null || !user.IsActive)
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsJsonAsync(new { error = "User not found or inactive" });
                        return;
                    }

                    // Optionally attach user to context items for easy access
                    context.Items["User"] = user;
                }
            }

            await _next(context);
        }
    }
}
