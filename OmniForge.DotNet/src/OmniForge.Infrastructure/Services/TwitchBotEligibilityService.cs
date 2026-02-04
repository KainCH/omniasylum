using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class TwitchBotEligibilityService : ITwitchBotEligibilityService
    {
        private readonly ITwitchApiService _twitchApiService;
        private readonly IOptions<TwitchSettings> _twitchSettings;
        private readonly IBotEligibilityCache _cache;
        private readonly ILogger<TwitchBotEligibilityService> _logger;

        public TwitchBotEligibilityService(
            ITwitchApiService twitchApiService,
            IOptions<TwitchSettings> twitchSettings,
            IBotEligibilityCache cache,
            ILogger<TwitchBotEligibilityService> logger)
        {
            _twitchApiService = twitchApiService;
            _twitchSettings = twitchSettings;
            _cache = cache;
            _logger = logger;
        }

        public async Task<BotEligibilityResult> GetEligibilityAsync(string broadcasterUserId, string broadcasterAccessToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(broadcasterUserId))
            {
                return new BotEligibilityResult(false, null, "Missing broadcaster user id");
            }

            if (string.IsNullOrWhiteSpace(broadcasterAccessToken))
            {
                return new BotEligibilityResult(false, null, "Missing broadcaster access token");
            }

            var botLoginOrId = _twitchSettings.Value.BotUsername;
            if (string.IsNullOrWhiteSpace(botLoginOrId))
            {
                return new BotEligibilityResult(false, null, "BotUsername is not configured");
            }

            var safeBroadcasterUserId = broadcasterUserId!;
            var safeBotLoginOrId = botLoginOrId!;

            // Cache to avoid calling Helix Get Moderators on every chat command.
            // In-memory cache only.
            var cacheTtl = TimeSpan.FromHours(3);
            var cached = await _cache.TryGetAsync(safeBroadcasterUserId, safeBotLoginOrId, cancellationToken);
            if (cached != null)
            {
                _logger.LogDebug("🧠 Bot eligibility cache hit. broadcaster_user_id={BroadcasterUserId}, useBot={UseBot}",
                    (safeBroadcasterUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    cached.UseBot);
                return cached;
            }

            try
            {
                _logger.LogInformation("🔎 Checking Forge bot moderator eligibility: broadcaster_user_id={BroadcasterUserId}, bot_login_or_id={BotLoginOrId}",
                    (safeBroadcasterUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (safeBotLoginOrId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));

                var moderatorsResponse = await _twitchApiService.GetModeratorsAsync(safeBroadcasterUserId!, broadcasterAccessToken, cancellationToken);

                _logger.LogInformation("📋 Helix Get Moderators result: broadcaster_user_id={BroadcasterUserId}, status={Status}, moderators_count={Count}",
                    (safeBroadcasterUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (int)moderatorsResponse.StatusCode,
                    moderatorsResponse.Moderators?.Count ?? 0);

                if (moderatorsResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    var result = new BotEligibilityResult(false, null, "Broadcaster token lacks required scope for moderators lookup (moderation:read)");
                    await _cache.SetAsync(safeBroadcasterUserId!, safeBotLoginOrId!, result, cacheTtl, cancellationToken);
                    return result;
                }

                if (moderatorsResponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var result = new BotEligibilityResult(false, null, "Unauthorized calling Helix Get Moderators (token invalid/mismatched client/user). User must re-login.");
                    await _cache.SetAsync(safeBroadcasterUserId!, safeBotLoginOrId!, result, cacheTtl, cancellationToken);
                    return result;
                }

                if (moderatorsResponse.StatusCode != HttpStatusCode.OK)
                {
                    var result = new BotEligibilityResult(false, null, $"Failed to check moderators ({(int)moderatorsResponse.StatusCode})");
                    await _cache.SetAsync(safeBroadcasterUserId!, safeBotLoginOrId!, result, cacheTtl, cancellationToken);
                    return result;
                }

                var botModerator = moderatorsResponse.FindModeratorByUserIdOrLogin(safeBotLoginOrId!);
                if (botModerator is null)
                {
                    var moderatorsPreview = string.Join(", ",
                        (moderatorsResponse.Moderators ?? new List<TwitchModeratorDto>())
                            .Take(20)
                            .Select(m => $"{(m.UserLogin ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n")}" +
                                         $"({(m.UserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n")})"));

                    _logger.LogInformation(
                        "🔍 Bot moderator check details: broadcaster_user_id={BroadcasterUserId}, bot_login_or_id={BotLoginOrId}, moderators=[{Moderators}]",
                        (safeBroadcasterUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                        (safeBotLoginOrId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                        (moderatorsPreview ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));

                    _logger.LogInformation("🚫 Forge bot is NOT a moderator for broadcaster_user_id={BroadcasterUserId}",
                        (safeBroadcasterUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                    var result = new BotEligibilityResult(false, null, "Bot is not a moderator in this channel");
                    await _cache.SetAsync(safeBroadcasterUserId!, safeBotLoginOrId!, result, cacheTtl, cancellationToken);
                    return result;
                }

                _logger.LogInformation("✅ Forge bot IS a moderator for broadcaster_user_id={BroadcasterUserId} (bot_user_id={BotUserId})",
                    (safeBroadcasterUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (botModerator.UserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                var ok = new BotEligibilityResult(true, botModerator.UserId, "Bot is a moderator");
                await _cache.SetAsync(safeBroadcasterUserId!, safeBotLoginOrId!, ok, cacheTtl, cancellationToken);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to determine bot eligibility for broadcaster {BroadcasterUserId}", (safeBroadcasterUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                var result = new BotEligibilityResult(false, null, "Error checking moderators");
                await _cache.SetAsync(safeBroadcasterUserId!, safeBotLoginOrId!, result, TimeSpan.FromSeconds(30), cancellationToken);
                return result;
            }
        }
    }
}
