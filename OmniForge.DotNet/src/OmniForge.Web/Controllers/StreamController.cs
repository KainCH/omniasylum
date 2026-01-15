using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize(AuthenticationSchemes = "Bearer,Cookies")]
    [ApiController]
    [Route("api/stream")]
    public class StreamController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly IStreamMonitorService _streamMonitorService;
        private readonly ITwitchClientManager _twitchClientManager;
        private readonly IGameContextRepository _gameContextRepository;
        private readonly IGameCountersRepository _gameCountersRepository;

        public StreamController(
            IUserRepository userRepository,
            ICounterRepository counterRepository,
            IOverlayNotifier overlayNotifier,
            IStreamMonitorService streamMonitorService,
            ITwitchClientManager twitchClientManager,
            IGameContextRepository gameContextRepository,
            IGameCountersRepository gameCountersRepository)
        {
            _userRepository = userRepository;
            _counterRepository = counterRepository;
            _overlayNotifier = overlayNotifier;
            _streamMonitorService = streamMonitorService;
            _twitchClientManager = twitchClientManager;
            _gameContextRepository = gameContextRepository;
            _gameCountersRepository = gameCountersRepository;
        }

        [HttpPost("status")]
        public async Task<IActionResult> UpdateStatus([FromBody] UpdateStatusRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var currentStatus = user.StreamStatus ?? "offline";
            var newStatus = currentStatus;

            switch (request.Action)
            {
                case "prep":
                    if (currentStatus == "offline") newStatus = "prepping";
                    break;
                case "go-live":
                    if (currentStatus == "prepping")
                    {
                        newStatus = "live";
                        await StartStreamInternal(userId);
                    }
                    break;
                case "end-stream":
                    if (currentStatus == "live" || currentStatus == "ending")
                    {
                        newStatus = "offline";
                        await EndStreamInternal(userId);
                    }
                    break;
                case "cancel-prep":
                    if (currentStatus == "prepping") newStatus = "offline";
                    break;
                default:
                    return BadRequest("Invalid action");
            }

            user.StreamStatus = newStatus;
            user.IsActive = newStatus == "prepping" || newStatus == "live";
            await _userRepository.SaveUserAsync(user);

            await _overlayNotifier.NotifyStreamStatusUpdateAsync(userId, newStatus);

            return Ok(new
            {
                message = $"Stream status updated to {newStatus}",
                streamStatus = newStatus,
                previousStatus = currentStatus
            });
        }

        [HttpGet("session")]
        public async Task<IActionResult> GetSession()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await _counterRepository.GetCountersAsync(userId);
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(new
            {
                isLive = counters?.StreamStarted != null,
                streamStarted = counters?.StreamStarted,
                counters = new
                {
                    deaths = counters?.Deaths ?? 0,
                    swears = counters?.Swears ?? 0,
                    bits = counters?.Bits ?? 0
                },
                settings = user.Features.StreamSettings
            });
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartStream()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await StartStreamInternal(userId);

            return Ok(new
            {
                message = "Stream started successfully",
                streamStarted = counters.StreamStarted,
                counters = counters
            });
        }

        [HttpPost("end")]
        public async Task<IActionResult> EndStream()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await EndStreamInternal(userId);

            return Ok(new
            {
                message = "Stream ended successfully",
                counters = counters
            });
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(user.Features.StreamSettings);
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] StreamSettings request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            // Validate thresholds
            if (request.BitThresholds != null)
            {
                if (request.BitThresholds.Death < 1) return BadRequest("Death threshold must be a positive number");
                if (request.BitThresholds.Swear < 1) return BadRequest("Swear threshold must be a positive number");
                if (request.BitThresholds.Celebration < 1) return BadRequest("Celebration threshold must be a positive number");
            }

            user.Features.StreamSettings = request;
            await _userRepository.SaveUserAsync(user);

            return Ok(user.Features.StreamSettings);
        }

        [HttpPost("reset-bits")]
        public async Task<IActionResult> ResetBits()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await _counterRepository.GetCountersAsync(userId);
            if (counters == null) return NotFound("Counters not found");

            int previousBits = counters.Bits;
            counters.Bits = 0;
            await _counterRepository.SaveCountersAsync(counters);

            // Notify overlay
            // Note: Node.js sends a 'counterUpdate' with a 'change' object.
            // The current IOverlayNotifier.NotifyCounterUpdateAsync sends the whole counter object.
            // We might need to enhance IOverlayNotifier if the frontend relies on 'change' for bits specifically.
            // For now, sending the updated counter is consistent with other methods.
            await _overlayNotifier.NotifyCounterUpdateAsync(userId, counters);

            return Ok(new
            {
                message = "Bits counter reset successfully",
                counters = counters
            });
        }

        [HttpGet("monitor/status")]
        public IActionResult GetMonitorStatus()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var status = _streamMonitorService.GetUserConnectionStatus(userId);
            var botStatus = _twitchClientManager.GetUserBotStatus(userId);

            return Ok(new
            {
                connected = status.Connected,
                subscriptions = status.Subscriptions,
                lastConnected = status.LastConnected,
                lastDiscordNotification = status.LastDiscordNotification,
                lastDiscordNotificationSuccess = status.LastDiscordNotificationSuccess,
                currentUserMonitored = _streamMonitorService.IsUserSubscribed(userId),
                twitchBot = botStatus
            });
        }

        [HttpPost("monitor/subscribe")]
        [HttpPost("monitor/start")]
        public async Task<IActionResult> SubscribeMonitor()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _streamMonitorService.SubscribeToUserAsync(userId);

            if (result == OmniForge.Core.Interfaces.SubscriptionResult.Success)
            {
                return Ok(new { message = "Successfully subscribed to stream monitoring", userId });
            }
            else if (result == OmniForge.Core.Interfaces.SubscriptionResult.RequiresReauth)
            {
                // Token is valid but missing required scopes - user must re-login
                return StatusCode(403, new
                {
                    error = "Your Twitch authorization is missing required permissions. Please re-login to grant updated permissions.",
                    requiresReauth = true,
                    redirectUrl = "/auth/twitch"
                });
            }
            else if (result == OmniForge.Core.Interfaces.SubscriptionResult.Unauthorized)
            {
                return Unauthorized(new { error = "Twitch authorization failed. Please re-login." });
            }

            return BadRequest(new { error = "Failed to subscribe to stream monitoring" });
        }

        [HttpPost("monitor/unsubscribe")]
        [HttpPost("monitor/stop")]
        public async Task<IActionResult> UnsubscribeMonitor()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _streamMonitorService.UnsubscribeFromUserAsync(userId);

            return Ok(new { message = "Successfully unsubscribed from stream monitoring", userId });
        }

        [HttpPost("monitor/reconnect")]
        public async Task<IActionResult> ReconnectMonitor()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _streamMonitorService.ForceReconnectUserAsync(userId);

            if (result == OmniForge.Core.Interfaces.SubscriptionResult.Success)
            {
                return Ok(new { message = "Successfully reconnected EventSub WebSocket", userId, status = "connected" });
            }
            else if (result == OmniForge.Core.Interfaces.SubscriptionResult.RequiresReauth)
            {
                // Token is valid but missing required scopes - user must re-login
                return StatusCode(403, new
                {
                    error = "Your Twitch authorization is missing required permissions. Please re-login to grant updated permissions.",
                    requiresReauth = true,
                    redirectUrl = "/auth/twitch"
                });
            }

            return StatusCode(500, new { error = "Failed to reconnect EventSub WebSocket", userId, status = "failed" });
        }

        [HttpGet("eventsub-status")]
        public async Task<IActionResult> GetEventSubStatus()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var status = _streamMonitorService.GetUserConnectionStatus(userId);
            var subscriptionsEnabled = !string.IsNullOrEmpty(user.DiscordChannelId) || !string.IsNullOrEmpty(user.DiscordWebhookUrl);

            return Ok(new
            {
                userId,
                username = user.Username,
                connectionStatus = new
                {
                    connected = status.Connected,
                    subscriptions = status.Subscriptions,
                    lastConnected = status.LastConnected
                },
                notificationSettings = user.DiscordSettings,
                // Backward compatibility: keep legacy field name while introducing channel-based config.
                discordWebhook = !string.IsNullOrEmpty(user.DiscordWebhookUrl),
                discordChannel = !string.IsNullOrEmpty(user.DiscordChannelId),
                discordConfigured = !string.IsNullOrEmpty(user.DiscordChannelId) || !string.IsNullOrEmpty(user.DiscordWebhookUrl),
                subscriptionsEnabled,
                timestamp = DateTimeOffset.UtcNow
            });
        }

        [HttpGet("bot/status")]
        public async Task<IActionResult> GetBotStatus()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var botStatus = _twitchClientManager.GetUserBotStatus(userId);
            var hasTokens = !string.IsNullOrEmpty(user.AccessToken);

            return Ok(new
            {
                userId,
                username = user.Username,
                bot = new
                {
                    connected = botStatus.Connected,
                    error = botStatus.Error,
                    reason = botStatus.Reason,
                    eligible = user.Features.ChatCommands && hasTokens,
                    chatCommandsEnabled = user.Features.ChatCommands,
                    hasTokens
                },
                timestamp = DateTimeOffset.UtcNow
            });
        }

        [HttpPost("bot/toggle")]
        public async Task<IActionResult> ToggleBot([FromBody] ToggleBotRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (request.Action == "start")
            {
                if (!user.Features.ChatCommands) return BadRequest(new { error = "Chat commands feature not enabled" });
                if (string.IsNullOrEmpty(user.AccessToken)) return BadRequest(new { error = "Missing Twitch access tokens" });

                await _twitchClientManager.ConnectUserAsync(userId);
            }
            else if (request.Action == "stop")
            {
                await _twitchClientManager.DisconnectUserAsync(userId);
            }
            else
            {
                return BadRequest(new { error = "Invalid action" });
            }

            var botStatus = _twitchClientManager.GetUserBotStatus(userId);

            return Ok(new
            {
                success = true,
                message = request.Action == "start" ? "Twitch bot started" : "Twitch bot stopped",
                action = request.Action,
                bot = botStatus,
                timestamp = DateTimeOffset.UtcNow
            });
        }

        // Phase 1 Endpoints
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var counters = await _counterRepository.GetCountersAsync(userId);

            return Ok(new
            {
                userId,
                username = user.Username,
                displayName = user.DisplayName,
                streamStatus = user.StreamStatus ?? "offline",
                isActive = user.IsActive,
                counters = counters,
                lastUpdated = DateTimeOffset.UtcNow
            });
        }

        [HttpPost("prep")]
        public async Task<IActionResult> PrepStream()
        {
            return await UpdateStatus(new UpdateStatusRequest { Action = "prep" });
        }

        [HttpPost("go-live")]
        public async Task<IActionResult> GoLive()
        {
            return await UpdateStatus(new UpdateStatusRequest { Action = "go-live" });
        }

        [HttpPost("end-stream")]
        public async Task<IActionResult> EndStreamPhase1()
        {
            return await UpdateStatus(new UpdateStatusRequest { Action = "end-stream" });
        }

        [HttpPost("cancel-prep")]
        public async Task<IActionResult> CancelPrep()
        {
            return await UpdateStatus(new UpdateStatusRequest { Action = "cancel-prep" });
        }

        private async Task<Counter> StartStreamInternal(string userId)
        {
            var now = DateTimeOffset.UtcNow;

            var counters = await _counterRepository.GetCountersAsync(userId) ?? new Counter { TwitchUserId = userId };

            // Best-effort: if we have an active game context and a saved counter snapshot for that game,
            // load it as the starting state for this stream.
            try
            {
                var ctx = await _gameContextRepository.GetAsync(userId);
                if (!string.IsNullOrWhiteSpace(ctx?.ActiveGameId))
                {
                    var savedForGame = await _gameCountersRepository.GetAsync(userId, ctx.ActiveGameId!);
                    if (savedForGame != null)
                    {
                        counters = savedForGame;
                        counters.TwitchUserId = userId;
                        if (!string.IsNullOrWhiteSpace(ctx.ActiveGameName))
                        {
                            counters.LastCategoryName = ctx.ActiveGameName;
                        }
                    }
                }
            }
            catch
            {
                // Best-effort only; stream start should still proceed.
            }

            counters.Bits = 0;
            counters.StreamStarted = now;
            counters.LastUpdated = now;

            await _counterRepository.SaveCountersAsync(counters);
            await _overlayNotifier.NotifyStreamStartedAsync(userId, counters);

            return counters;
        }

        private async Task<Counter> EndStreamInternal(string userId)
        {
            var now = DateTimeOffset.UtcNow;

            var counters = await _counterRepository.GetCountersAsync(userId);
            if (counters == null)
            {
                counters = new Counter { TwitchUserId = userId };
            }

            GameContext? ctx = null;
            try
            {
                ctx = await _gameContextRepository.GetAsync(userId);
            }
            catch
            {
                // Best-effort only; stream end should still proceed.
            }

            counters.StreamStarted = null;
            counters.LastUpdated = now;
            if (!string.IsNullOrWhiteSpace(ctx?.ActiveGameName))
            {
                counters.LastCategoryName = ctx.ActiveGameName;
            }
            // Clear last notified stream ID if we were tracking it (not implemented in Counter entity yet, but logic is in JS)

            await _counterRepository.SaveCountersAsync(counters);

            // Best-effort per-game save so the game-detected counter system has an up-to-date snapshot at stream end.
            try
            {
                if (!string.IsNullOrWhiteSpace(ctx?.ActiveGameId))
                {
                    await _gameCountersRepository.SaveAsync(userId, ctx.ActiveGameId!, counters);
                }
            }
            catch
            {
                // Best-effort only; failing to save per-game counters should not break end-stream.
            }
            await _overlayNotifier.NotifyStreamEndedAsync(userId, counters);

            return counters;
        }
    }

    public class UpdateStatusRequest
    {
        public string Action { get; set; } = string.Empty;
    }

    public class ToggleBotRequest
    {
        public string Action { get; set; } = string.Empty;
    }
}
