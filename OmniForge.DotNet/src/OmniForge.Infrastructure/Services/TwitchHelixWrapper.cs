using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Interfaces;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;

using TwitchLib.Api.Core.Enums; // Added

namespace OmniForge.Infrastructure.Services
{
    [ExcludeFromCodeCoverage]
    public class TwitchHelixWrapper : ITwitchHelixWrapper
    {
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
            var api = new TwitchAPI();
            api.Settings.ClientId = clientId;
            api.Settings.AccessToken = accessToken;
            await api.Helix.EventSub.CreateEventSubSubscriptionAsync(type, version, condition, method, sessionId);
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
