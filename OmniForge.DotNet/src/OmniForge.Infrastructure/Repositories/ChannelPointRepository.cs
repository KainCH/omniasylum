using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class ChannelPointRepository : IChannelPointRepository
    {
        private readonly TableClient _tableClient;

        public ChannelPointRepository(TableServiceClient tableServiceClient)
        {
            _tableClient = tableServiceClient.GetTableClient("counters");
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<IEnumerable<ChannelPointReward>> GetRewardsAsync(string userId)
        {
            var rewards = new List<ChannelPointReward>();

            // Filter by PartitionKey (UserId) and RowKey prefix "reward-"
            // Note: Azure Table Storage doesn't support "StartsWith" in OData efficiently for RowKey in all SDK versions,
            // but we can use a range query: RowKey >= "reward-" and RowKey < "reward." (since '.' is next ascii char after '-')
            // Or just iterate and filter client side if the number of rows per user is small (which it is).
            // A more robust way is using the CompareTo logic in the filter string.

            var filter = $"PartitionKey eq '{userId}' and RowKey ge 'reward-' and RowKey lt 'reward.'";

            var query = _tableClient.QueryAsync<ChannelPointRewardTableEntity>(filter);

            await foreach (var entity in query)
            {
                rewards.Add(entity.ToChannelPointReward());
            }

            return rewards;
        }

        public async Task<ChannelPointReward?> GetRewardAsync(string userId, string rewardId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<ChannelPointRewardTableEntity>(userId, $"reward-{rewardId}");
                return response.Value.ToChannelPointReward();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SaveRewardAsync(ChannelPointReward reward)
        {
            var entity = ChannelPointRewardTableEntity.FromChannelPointReward(reward);
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task DeleteRewardAsync(string userId, string rewardId)
        {
            await _tableClient.DeleteEntityAsync(userId, $"reward-{rewardId}");
        }
    }
}
