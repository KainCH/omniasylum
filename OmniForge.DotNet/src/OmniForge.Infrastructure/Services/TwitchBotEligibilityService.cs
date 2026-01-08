using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;

namespace OmniForge.Infrastructure.Services
{
    public class TwitchBotEligibilityService : ITwitchBotEligibilityService
    {
        private readonly ITwitchApiService _twitchApiService;
        private readonly IOptions<TwitchSettings> _twitchSettings;
        private readonly ILogger<TwitchBotEligibilityService> _logger;

        public TwitchBotEligibilityService(
            ITwitchApiService twitchApiService,
            IOptions<TwitchSettings> twitchSettings,
            ILogger<TwitchBotEligibilityService> logger)
        {
            _twitchApiService = twitchApiService;
            _twitchSettings = twitchSettings;
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

            try
            {
                _logger.LogInformation("🔎 Checking Forge bot moderator eligibility: broadcaster_user_id={BroadcasterUserId}, bot_login_or_id={BotLoginOrId}",
                    OmniForge.Core.Utilities.LogSanitizer.Sanitize(broadcasterUserId),
                    OmniForge.Core.Utilities.LogSanitizer.Sanitize(botLoginOrId));

                var moderatorsResponse = await _twitchApiService.GetModeratorsAsync(broadcasterUserId, broadcasterAccessToken, cancellationToken);

                _logger.LogInformation("📋 Helix Get Moderators result: broadcaster_user_id={BroadcasterUserId}, status={Status}, moderators_count={Count}",
                    OmniForge.Core.Utilities.LogSanitizer.Sanitize(broadcasterUserId),
                    (int)moderatorsResponse.StatusCode,
                    moderatorsResponse.Moderators?.Count ?? 0);

                if (moderatorsResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new BotEligibilityResult(false, null, "Broadcaster token lacks required scope for moderators lookup (moderation:read)");
                }

                if (moderatorsResponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new BotEligibilityResult(false, null, "Unauthorized calling Helix Get Moderators (token invalid/mismatched client/user). User must re-login.");
                }

                if (moderatorsResponse.StatusCode != HttpStatusCode.OK)
                {
                    return new BotEligibilityResult(false, null, $"Failed to check moderators ({(int)moderatorsResponse.StatusCode})");
                }

                var botModerator = moderatorsResponse.FindModeratorByUserIdOrLogin(botLoginOrId);
                if (botModerator is null)
                {
                    var moderatorsPreview = string.Join(", ",
                        (moderatorsResponse.Moderators ?? new List<TwitchModeratorDto>())
                            .Take(20)
                            .Select(m => $"{OmniForge.Core.Utilities.LogSanitizer.Sanitize(m.UserLogin)}({OmniForge.Core.Utilities.LogSanitizer.Sanitize(m.UserId)})"));

                    _logger.LogInformation(
                        "🔍 Bot moderator check details: broadcaster_user_id={BroadcasterUserId}, bot_login_or_id={BotLoginOrId}, moderators=[{Moderators}]",
                        OmniForge.Core.Utilities.LogSanitizer.Sanitize(broadcasterUserId),
                        OmniForge.Core.Utilities.LogSanitizer.Sanitize(botLoginOrId),
                        moderatorsPreview);

                    _logger.LogInformation("🚫 Forge bot is NOT a moderator for broadcaster_user_id={BroadcasterUserId}",
                        OmniForge.Core.Utilities.LogSanitizer.Sanitize(broadcasterUserId));
                    return new BotEligibilityResult(false, null, "Bot is not a moderator in this channel");
                }

                _logger.LogInformation("✅ Forge bot IS a moderator for broadcaster_user_id={BroadcasterUserId} (bot_user_id={BotUserId})",
                    OmniForge.Core.Utilities.LogSanitizer.Sanitize(broadcasterUserId),
                    OmniForge.Core.Utilities.LogSanitizer.Sanitize(botModerator.UserId));
                return new BotEligibilityResult(true, botModerator.UserId, "Bot is a moderator");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to determine bot eligibility for broadcaster {BroadcasterUserId}", broadcasterUserId);
                return new BotEligibilityResult(false, null, "Error checking moderators");
            }
        }
    }
}
