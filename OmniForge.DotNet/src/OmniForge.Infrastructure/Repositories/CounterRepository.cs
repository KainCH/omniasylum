using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class CounterRepository : ICounterRepository
    {
        private readonly TableClient _tableClient;

        public CounterRepository(TableServiceClient tableServiceClient)
        {
            _tableClient = tableServiceClient.GetTableClient("counters");
            _tableClient.CreateIfNotExists();
        }

        public async Task<Counter> GetCountersAsync(string twitchUserId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<CounterTableEntity>(twitchUserId, "counters");
                return response.Value.ToDomain();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Return default counters if not found
                return new Counter
                {
                    TwitchUserId = twitchUserId,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }
        }

        public async Task SaveCountersAsync(Counter counter)
        {
            var entity = CounterTableEntity.FromDomain(counter);
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }
    }
}
