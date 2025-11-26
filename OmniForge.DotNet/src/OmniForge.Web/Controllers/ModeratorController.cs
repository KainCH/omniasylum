using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System.Security.Claims;

namespace OmniForge.Web.Controllers
{
    [Route("api/moderator")]
    [ApiController]
    [Authorize]
    public class ModeratorController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly ISeriesRepository _seriesRepository;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<ModeratorController> _logger;

        public ModeratorController(
            IUserRepository userRepository,
            ICounterRepository counterRepository,
            ISeriesRepository seriesRepository,
            IOverlayNotifier overlayNotifier,
            ILogger<ModeratorController> logger)
        {
            _userRepository = userRepository;
            _counterRepository = counterRepository;
            _seriesRepository = seriesRepository;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        private string GetCurrentUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }

        private string GetCurrentUsername()
        {
            return User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        }

        private async Task<bool> CanModeratorManageUserAsync(string moderatorId, string streamerId)
        {
            var moderator = await _userRepository.GetUserAsync(moderatorId);
            return moderator != null && moderator.ManagedStreamers.Contains(streamerId);
        }

        [HttpGet("my-moderators")]
        public async Task<IActionResult> GetMyModerators()
        {
            try
            {
                var userId = GetCurrentUserId();
                var allUsers = await _userRepository.GetAllUsersAsync();

                var moderators = allUsers
                    .Where(u => u.ManagedStreamers.Contains(userId))
                    .Select(u => new
                    {
                        u.TwitchUserId,
                        u.Username,
                        u.DisplayName,
                        u.ProfileImageUrl
                    })
                    .ToList();

                _logger.LogInformation("📋 User {Username} listed their moderators", GetCurrentUsername());

                return Ok(new { moderators });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching moderators");
                return StatusCode(500, new { error = "Failed to fetch moderators" });
            }
        }

        [HttpPost("grant-access")]
        public async Task<IActionResult> GrantAccess([FromBody] GrantAccessRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var moderatorId = request.ModeratorId;

                if (string.IsNullOrEmpty(moderatorId))
                {
                    return BadRequest(new { error = "Moderator ID is required" });
                }

                if (userId == moderatorId)
                {
                    return BadRequest(new { error = "You cannot add yourself as a moderator" });
                }

                var moderator = await _userRepository.GetUserAsync(moderatorId);
                if (moderator == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                if (!moderator.ManagedStreamers.Contains(userId))
                {
                    moderator.ManagedStreamers.Add(userId);
                    await _userRepository.SaveUserAsync(moderator);
                }

                _logger.LogInformation("✅ User {Username} granted moderator access to {ModeratorUsername}", GetCurrentUsername(), moderator.Username);

                return Ok(new
                {
                    message = "Moderator access granted",
                    moderator = new
                    {
                        moderator.TwitchUserId,
                        moderator.Username,
                        moderator.DisplayName,
                        moderator.ProfileImageUrl
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error granting moderator access");
                return StatusCode(500, new { error = "Failed to grant moderator access" });
            }
        }

        [HttpPost("revoke-access")]
        public async Task<IActionResult> RevokeAccess([FromBody] RevokeAccessRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var moderatorId = request.ModeratorId;

                if (string.IsNullOrEmpty(moderatorId))
                {
                    return BadRequest(new { error = "Moderator ID is required" });
                }

                var moderator = await _userRepository.GetUserAsync(moderatorId);
                if (moderator == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                if (moderator.ManagedStreamers.Contains(userId))
                {
                    moderator.ManagedStreamers.Remove(userId);
                    await _userRepository.SaveUserAsync(moderator);
                }

                _logger.LogInformation("🚫 User {Username} revoked moderator access from {ModeratorUsername}", GetCurrentUsername(), moderator.Username);

                return Ok(new { message = "Moderator access revoked" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error revoking moderator access");
                return StatusCode(500, new { error = "Failed to revoke moderator access" });
            }
        }

        [HttpGet("search-users")]
        public async Task<IActionResult> SearchUsers([FromQuery] string query)
        {
            try
            {
                if (string.IsNullOrEmpty(query) || query.Length < 2)
                {
                    return BadRequest(new { error = "Search query must be at least 2 characters" });
                }

                var allUsers = await _userRepository.GetAllUsersAsync();
                var users = allUsers
                    .Where(u => u.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(10)
                    .Select(u => new
                    {
                        u.TwitchUserId,
                        u.Username,
                        u.DisplayName,
                        u.ProfileImageUrl
                    })
                    .ToList();

                return Ok(new { users });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error searching users");
                return StatusCode(500, new { error = "Failed to search users" });
            }
        }

        [HttpGet("managed-streamers")]
        public async Task<IActionResult> GetManagedStreamers()
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _userRepository.GetUserAsync(userId);

                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var managedStreamers = new List<object>();
                foreach (var streamerId in user.ManagedStreamers)
                {
                    var streamer = await _userRepository.GetUserAsync(streamerId);
                    if (streamer != null)
                    {
                        managedStreamers.Add(new
                        {
                            streamer.TwitchUserId,
                            streamer.Username,
                            streamer.DisplayName,
                            streamer.ProfileImageUrl,
                            streamer.StreamStatus
                        });
                    }
                }

                _logger.LogInformation("📋 Moderator {Username} listed managed streamers", GetCurrentUsername());

                return Ok(new { streamers = managedStreamers });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching managed streamers");
                return StatusCode(500, new { error = "Failed to fetch managed streamers" });
            }
        }

        [HttpGet("streamers/{streamerId}/features")]
        public async Task<IActionResult> GetStreamerFeatures(string streamerId)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                _logger.LogInformation("👀 Moderator {Username} viewed features for streamer {StreamerUsername}", GetCurrentUsername(), streamer.Username);

                return Ok(new
                {
                    features = streamer.Features,
                    streamer = new { userId = streamerId, username = streamer.Username }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching streamer features");
                return StatusCode(500, new { error = "Failed to fetch features" });
            }
        }

        [HttpPut("streamers/{streamerId}/features")]
        public async Task<IActionResult> UpdateStreamerFeatures(string streamerId, [FromBody] FeatureFlags features)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                streamer.Features = features;
                await _userRepository.SaveUserAsync(streamer);

                _logger.LogInformation("✅ Moderator {Username} updated features for streamer {StreamerUsername}", GetCurrentUsername(), streamer.Username);

                return Ok(new
                {
                    message = "Features updated successfully",
                    features = streamer.Features,
                    streamer = new { userId = streamerId, username = streamer.Username },
                    moderator = GetCurrentUsername()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating streamer features");
                return StatusCode(500, new { error = "Failed to update features" });
            }
        }

        [HttpGet("streamers/{streamerId}/overlay")]
        public async Task<IActionResult> GetStreamerOverlay(string streamerId)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                _logger.LogInformation("👀 Moderator {Username} viewed overlay settings for streamer {StreamerUsername}", GetCurrentUsername(), streamer.Username);

                return Ok(new
                {
                    overlay = streamer.OverlaySettings,
                    streamer = new { userId = streamerId, username = streamer.Username }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching streamer overlay");
                return StatusCode(500, new { error = "Failed to fetch overlay settings" });
            }
        }

        [HttpPut("streamers/{streamerId}/overlay")]
        public async Task<IActionResult> UpdateStreamerOverlay(string streamerId, [FromBody] OverlaySettings overlay)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                streamer.OverlaySettings = overlay;
                await _userRepository.SaveUserAsync(streamer);

                // Notify overlay clients
                await _overlayNotifier.NotifySettingsUpdateAsync(streamerId, overlay);

                _logger.LogInformation("✅ Moderator {Username} updated overlay settings for streamer {StreamerUsername}", GetCurrentUsername(), streamer.Username);

                return Ok(new
                {
                    message = "Overlay settings updated successfully",
                    overlay = streamer.OverlaySettings,
                    streamer = new { userId = streamerId, username = streamer.Username },
                    moderator = GetCurrentUsername()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating streamer overlay");
                return StatusCode(500, new { error = "Failed to update overlay settings" });
            }
        }

        [HttpGet("streamers/{streamerId}/discord-webhook")]
        public async Task<IActionResult> GetStreamerDiscordWebhook(string streamerId)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                _logger.LogInformation("👀 Moderator {Username} viewed Discord webhook for streamer {StreamerUsername}", GetCurrentUsername(), streamer.Username);

                return Ok(new
                {
                    webhookUrl = streamer.DiscordWebhookUrl,
                    enabled = streamer.Features.DiscordNotifications,
                    streamer = new { userId = streamerId, username = streamer.Username }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching streamer Discord webhook");
                return StatusCode(500, new { error = "Failed to fetch Discord webhook" });
            }
        }

        [HttpPut("streamers/{streamerId}/discord-webhook")]
        public async Task<IActionResult> UpdateStreamerDiscordWebhook(string streamerId, [FromBody] UpdateDiscordWebhookRequest request)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                streamer.DiscordWebhookUrl = request.WebhookUrl;
                streamer.Features.DiscordNotifications = request.Enabled;
                await _userRepository.SaveUserAsync(streamer);

                _logger.LogInformation("✅ Moderator {Username} updated Discord webhook for streamer {StreamerUsername}", GetCurrentUsername(), streamer.Username);

                return Ok(new
                {
                    message = "Discord webhook updated successfully",
                    webhookUrl = streamer.DiscordWebhookUrl,
                    enabled = streamer.Features.DiscordNotifications,
                    streamer = new { userId = streamerId, username = streamer.Username },
                    moderator = GetCurrentUsername()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating streamer Discord webhook");
                return StatusCode(500, new { error = "Failed to update Discord webhook" });
            }
        }

        [HttpGet("streamers/{streamerId}/series-saves")]
        public async Task<IActionResult> GetStreamerSeriesSaves(string streamerId)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                var seriesSaves = (await _seriesRepository.GetSeriesAsync(streamerId)).ToList();

                _logger.LogInformation("📋 Moderator {Username} listed {Count} series saves for streamer {StreamerUsername}", GetCurrentUsername(), seriesSaves.Count, streamer.Username);

                return Ok(new
                {
                    seriesSaves,
                    streamer = new { userId = streamerId, username = streamer.Username },
                    moderator = GetCurrentUsername()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching streamer series saves");
                return StatusCode(500, new { error = "Failed to fetch series saves" });
            }
        }

        [HttpPost("streamers/{streamerId}/series-saves")]
        public async Task<IActionResult> CreateStreamerSeriesSave(string streamerId, [FromBody] CreateSeriesRequest request)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                if (string.IsNullOrWhiteSpace(request.SeriesName))
                {
                    return BadRequest(new { error = "Series name is required" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                var currentCounters = await _counterRepository.GetCountersAsync(streamerId);
                if (currentCounters == null) return NotFound(new { error = "Counters not found" });

                var series = new Series
                {
                    UserId = streamerId,
                    Name = request.SeriesName.Trim(),
                    Description = request.Description ?? "",
                    Snapshot = new Counter
                    {
                        Deaths = currentCounters.Deaths,
                        Swears = currentCounters.Swears,
                        Screams = currentCounters.Screams,
                        Bits = currentCounters.Bits
                    },
                    CreatedAt = DateTimeOffset.UtcNow
                };

                await _seriesRepository.CreateSeriesAsync(series);

                _logger.LogInformation("✅ Moderator {Username} created series save \"{SeriesName}\" for streamer {StreamerUsername}", GetCurrentUsername(), series.Name, streamer.Username);

                return Ok(new
                {
                    message = "Series save created successfully",
                    seriesSave = series,
                    streamer = new { userId = streamerId, username = streamer.Username },
                    moderator = GetCurrentUsername()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating series save");
                return StatusCode(500, new { error = "Failed to create series save" });
            }
        }

        [HttpPost("streamers/{streamerId}/series-saves/{seriesId}/load")]
        public async Task<IActionResult> LoadStreamerSeriesSave(string streamerId, string seriesId)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                var series = await _seriesRepository.GetSeriesByIdAsync(streamerId, seriesId);
                if (series == null)
                {
                    return NotFound(new { error = "Series save not found" });
                }

                // Load the series (update current counters)
                var counters = await _counterRepository.GetCountersAsync(streamerId);
                if (counters == null) return NotFound(new { error = "Counters not found" });

                counters.Deaths = series.Snapshot.Deaths;
                counters.Swears = series.Snapshot.Swears;
                counters.Screams = series.Snapshot.Screams;
                counters.Bits = series.Snapshot.Bits;

                await _counterRepository.SaveCountersAsync(counters);

                // Broadcast update
                await _overlayNotifier.NotifyCounterUpdateAsync(streamerId, counters);

                _logger.LogInformation("🔄 Moderator {Username} loaded series save {SeriesId} for streamer {StreamerUsername}", GetCurrentUsername(), seriesId, streamer.Username);

                return Ok(new
                {
                    message = "Series save loaded successfully",
                    counters,
                    seriesId,
                    streamer = new { userId = streamerId, username = streamer.Username },
                    moderator = GetCurrentUsername()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error loading series save");
                return StatusCode(500, new { error = "Failed to load series save" });
            }
        }

        [HttpDelete("streamers/{streamerId}/series-saves/{seriesId}")]
        public async Task<IActionResult> DeleteStreamerSeriesSave(string streamerId, string seriesId)
        {
            try
            {
                var moderatorId = GetCurrentUserId();
                if (!await CanModeratorManageUserAsync(moderatorId, streamerId))
                {
                    return StatusCode(403, new { error = "You do not have permission to manage this streamer" });
                }

                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer == null)
                {
                    return NotFound(new { error = "Streamer not found" });
                }

                await _seriesRepository.DeleteSeriesAsync(streamerId, seriesId);

                _logger.LogInformation("🗑️ Moderator {Username} deleted series save {SeriesId} for streamer {StreamerUsername}", GetCurrentUsername(), seriesId, streamer.Username);

                return Ok(new
                {
                    message = "Series save deleted successfully",
                    seriesId,
                    streamer = new { userId = streamerId, username = streamer.Username },
                    moderator = GetCurrentUsername()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting series save");
                return StatusCode(500, new { error = "Failed to delete series save" });
            }
        }

        public class GrantAccessRequest
        {
            public string ModeratorId { get; set; } = string.Empty;
        }

        public class RevokeAccessRequest
        {
            public string ModeratorId { get; set; } = string.Empty;
        }

        public class UpdateDiscordWebhookRequest
        {
            public string WebhookUrl { get; set; } = string.Empty;
            public bool Enabled { get; set; }
        }

        public class CreateSeriesRequest
        {
            public string SeriesName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }
    }
}
