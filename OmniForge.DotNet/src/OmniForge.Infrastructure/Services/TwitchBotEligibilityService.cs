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
                var moderatorsResponse = await _twitchApiService.GetModeratorsAsync(broadcasterUserId, broadcasterAccessToken, cancellationToken);

                if (moderatorsResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new BotEligibilityResult(false, null, "Broadcaster token lacks moderation:read (cannot check moderators)");
                }

                if (moderatorsResponse.StatusCode != HttpStatusCode.OK)
                {
                    return new BotEligibilityResult(false, null, $"Failed to check moderators ({(int)moderatorsResponse.StatusCode})");
                }

                var botModerator = moderatorsResponse.FindModeratorByUserIdOrLogin(botLoginOrId);
                if (botModerator is null)
                {
                    return new BotEligibilityResult(false, null, "Bot is not a moderator in this channel");
                }

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
