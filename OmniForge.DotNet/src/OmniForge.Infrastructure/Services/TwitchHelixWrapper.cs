using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Core.Utilities;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;

using TwitchLib.Api.Core.Enums; // Added

using Microsoft.Extensions.Logging; // Added

namespace OmniForge.Infrastructure.Services
{
    [ExcludeFromCodeCoverage]
    public class TwitchHelixWrapper : ITwitchHelixWrapper
    {
        private readonly ILogger<TwitchHelixWrapper> _logger;

        public TwitchHelixWrapper(ILogger<TwitchHelixWrapper> logger)
        {
            _logger = logger;
        }

        public async Task<List<HelixCustomReward>> GetCustomRewardsAsync(string clientId, string accessToken, string broadcasterId)
        {
            var api = new TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessToken;
            var response = await api.Helix.ChannelPoints.GetCustomRewardAsync(broadcasterId);

            return response.Data.Select(MapToEntity).ToList();
        }

        public async Task<List<HelixCustomReward>> CreateCustomRewardAsync(string clientId, string accessToken, string broadcasterId, CreateCustomRewardsRequest request)
        {
            var api = new TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessToken;
            var response = await api.Helix.ChannelPoints.CreateCustomRewardsAsync(broadcasterId, request);

            return response.Data.Select(MapToEntity).ToList();
        }

        public async Task DeleteCustomRewardAsync(string clientId, string accessToken, string broadcasterId, string rewardId)
        {
            var api = new TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessToken;
            await api.Helix.ChannelPoints.DeleteCustomRewardAsync(broadcasterId, rewardId);
        }

        public async Task CreateEventSubSubscriptionAsync(string clientId, string accessToken, string type, string version, Dictionary<string, string> condition, EventSubTransportMethod method, string sessionId)
        {
            try
            {
                _logger.LogInformation("Creating EventSub Subscription: Type={Type}, Version={Version}, SessionId={SessionId}, ClientId={ClientId}, TokenLength={TokenLength}",
                    LogSanitizer.Sanitize(type), LogSanitizer.Sanitize(version), LogSanitizer.Sanitize(sessionId), LogSanitizer.Sanitize(clientId), accessToken?.Length ?? 0);

                if (condition != null)
                {
                    foreach (var kvp in condition)
                    {
                        _logger.LogInformation("EventSub Condition: {Key}={Value}", LogSanitizer.Sanitize(kvp.Key), LogSanitizer.Sanitize(kvp.Value));
                    }
                }

                var api = new TwitchAPI();
                api.Settings.ClientId = clientId;
                api.Settings.AccessToken = accessToken;

                await api.Helix.EventSub.CreateEventSubSubscriptionAsync(type, version, condition, method, sessionId);
            }
            catch (TwitchLib.Api.Core.Exceptions.BadRequestException ex)
            {
                _logger.LogError(ex, "BadRequestException in CreateEventSubSubscriptionAsync: {Message}", LogSanitizer.Sanitize(ex.Message));
                // Log the raw response body if possible, or at least the parameters again
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateEventSubSubscriptionAsync");
                throw;
            }
        }

        public async Task<GetStreamsResponse> GetStreamsAsync(string clientId, string accessToken, List<string> userIds)
        {
            var api = new TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessToken;
            return await api.Helix.Streams.GetStreamsAsync(userIds: userIds);
        }

        public async Task<TwitchLib.Api.Helix.Models.Clips.CreateClip.CreatedClipResponse> CreateClipAsync(string clientId, string accessToken, string broadcasterId)
        {
            var api = new TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessToken;
            return await api.Helix.Clips.CreateClipAsync(broadcasterId);
        }

        public async Task<TwitchLib.Api.Helix.Models.Channels.GetChannelInformation.GetChannelInformationResponse> GetChannelInformationAsync(string clientId, string accessToken, string broadcasterId)
        {
            var api = new TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessToken;
            return await api.Helix.Channels.GetChannelInformationAsync(broadcasterId);
        }

        private static HelixCustomReward MapToEntity(TwitchLib.Api.Helix.Models.ChannelPoints.CustomReward reward)
        {
            return new HelixCustomReward
            {
                Id = reward.Id,
                Title = reward.Title,
                Cost = reward.Cost,
                Prompt = reward.Prompt,
                IsEnabled = reward.IsEnabled,
                BackgroundColor = reward.BackgroundColor,
                IsUserInputRequired = reward.IsUserInputRequired,
                MaxPerStreamSetting = new HelixMaxPerStreamSetting
                {
                    IsEnabled = reward.MaxPerStreamSetting.IsEnabled,
                    MaxPerStream = reward.MaxPerStreamSetting.MaxPerStream
                },
                MaxPerUserPerStreamSetting = new HelixMaxPerUserPerStreamSetting
                {
                    IsEnabled = reward.MaxPerUserPerStreamSetting.IsEnabled,
                    MaxPerUserPerStream = reward.MaxPerUserPerStreamSetting.MaxPerUserPerStream
                },
                GlobalCooldownSetting = new HelixGlobalCooldownSetting
                {
                    IsEnabled = reward.GlobalCooldownSetting.IsEnabled,
                    GlobalCooldownSeconds = reward.GlobalCooldownSetting.GlobalCooldownSeconds
                }
            };
        }
    }
}
