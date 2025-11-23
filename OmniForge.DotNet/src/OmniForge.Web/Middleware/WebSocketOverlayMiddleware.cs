using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Middleware
{
    public class WebSocketOverlayMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IWebSocketOverlayManager _webSocketManager;

        public WebSocketOverlayMiddleware(RequestDelegate next, IWebSocketOverlayManager webSocketManager)
        {
            _next = next;
            _webSocketManager = webSocketManager;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path == "/ws/overlay")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    var userId = context.Request.Query["userId"].ToString();
                    if (string.IsNullOrEmpty(userId))
                    {
                        context.Response.StatusCode = 400;
                        return;
                    }

                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await _webSocketManager.HandleConnectionAsync(userId, webSocket);
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            }
            else
            {
                await _next(context);
            }
        }
    }
}
