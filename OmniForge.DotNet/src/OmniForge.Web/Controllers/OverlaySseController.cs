using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Services;

namespace OmniForge.Web.Controllers
{
    [ApiController]
    [Route("api/v2/overlay")]
    public class OverlaySseController : ControllerBase
    {
        private readonly SseConnectionManager _sseManager;
        private readonly IUserRepository _userRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly IAlertRepository _alertRepository;
        private readonly IStreamMonitorService _streamMonitor;
        private readonly ILogger<OverlaySseController> _logger;

        public OverlaySseController(
            SseConnectionManager sseManager,
            IUserRepository userRepository,
            ICounterRepository counterRepository,
            IAlertRepository alertRepository,
            IStreamMonitorService streamMonitor,
            ILogger<OverlaySseController> logger)
        {
            _sseManager = sseManager;
            _userRepository = userRepository;
            _counterRepository = counterRepository;
            _alertRepository = alertRepository;
            _streamMonitor = streamMonitor;
            _logger = logger;
        }

        /// <summary>
        /// SSE event stream. Overlay opens this as an EventSource.
        /// Sends event: connected with connectionId, then holds open for incremental events.
        /// </summary>
        [HttpGet("events")]
        public async Task GetEventStream([FromQuery] string userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                Response.StatusCode = 400;
                return;
            }

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null || !user.Features.StreamOverlay)
            {
                Response.StatusCode = 403;
                return;
            }

            var tier = HttpContext.Request.Query["tier"].FirstOrDefault();
            bool isV2 = string.Equals(tier, "v2", StringComparison.OrdinalIgnoreCase);

            if (isV2 && !user.Features.OverlayV2)
            {
                Response.StatusCode = 403;
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";
            Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering

            var connectionId = await _sseManager.RegisterAsync(userId, Response.Body, ct, isV2);

            // Hold connection open until client disconnects
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — expected
            }
            finally
            {
                _sseManager.RemoveClient(userId, connectionId);
            }
        }

        /// <summary>
        /// Ready signal. Client calls this after DOM is set up.
        /// Server responds by sending event: init with full config bundle to this connection.
        /// </summary>
        [HttpPost("ready")]
        public async Task<IActionResult> SignalReady([FromQuery] string userId, [FromQuery] string connectionId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(connectionId))
            {
                return BadRequest(new { error = "userId and connectionId are required" });
            }

            try
            {
                var user = await _userRepository.GetUserAsync(userId);
                if (user == null || !user.Features.StreamOverlay)
                    return StatusCode(403, new { error = "Stream overlay not enabled" });

                var counter = await _counterRepository.GetCountersAsync(userId);
                var alerts = await _alertRepository.GetAlertsAsync(userId);
                var customCountersConfig = await _counterRepository.GetCustomCountersConfigAsync(userId);

                // Determine stream live status: in-memory check first, DB fallback
                var isLive = _streamMonitor.IsUserLive(userId);
                if (!isLive && counter?.StreamStarted != null)
                {
                    isLive = true; // DB says stream is live
                }

                var initBundle = new
                {
                    settings = user?.OverlaySettings,
                    counters = counter != null ? new
                    {
                        counter.Deaths,
                        counter.Swears,
                        counter.Screams,
                        counter.Bits,
                        counter.CustomCounters,
                        counter.StreamStarted
                    } : null,
                    customCountersConfig = customCountersConfig?.Counters,
                    alerts,
                    streamStatus = isLive ? "live" : "offline",
                    streamStarted = counter?.StreamStarted,
                    bitsGoal = user?.OverlaySettings?.BitsGoal,
                    serverInstanceId = ServerInstance.Id
                };

                var sent = await _sseManager.SendToConnectionAsync(userId, connectionId, "init", initBundle);
                if (!sent)
                {
                    return NotFound(new { error = "Connection not found" });
                }

                _logger.LogInformation("SSE init sent: user_id={UserId}, connection_id={ConnectionId}", userId, connectionId);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SSE init for user_id={UserId}", userId);
                return StatusCode(500, new { error = "Failed to send init bundle" });
            }
        }
    }
}
