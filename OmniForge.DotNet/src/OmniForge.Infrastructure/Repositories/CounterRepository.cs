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

        public async Task<Counter> IncrementCounterAsync(string userId, string counterType)
        {
            var counter = await GetCountersAsync(userId);

            switch (counterType.ToLower())
            {
                case "deaths":
                    counter.Deaths++;
                    break;
                case "swears":
                    counter.Swears++;
                    break;
                case "screams":
                    counter.Screams++;
                    break;
                default:
                    throw new ArgumentException("Invalid counter type", nameof(counterType));
            }

            counter.LastUpdated = DateTimeOffset.UtcNow;
            await SaveCountersAsync(counter);
            return counter;
        }

        public async Task<Counter> DecrementCounterAsync(string userId, string counterType)
        {
            var counter = await GetCountersAsync(userId);

            switch (counterType.ToLower())
            {
                case "deaths":
                    counter.Deaths = Math.Max(0, counter.Deaths - 1);
                    break;
                case "swears":
                    counter.Swears = Math.Max(0, counter.Swears - 1);
                    break;
                case "screams":
                    counter.Screams = Math.Max(0, counter.Screams - 1);
                    break;
                default:
                    throw new ArgumentException("Invalid counter type", nameof(counterType));
            }

            counter.LastUpdated = DateTimeOffset.UtcNow;
            await SaveCountersAsync(counter);
            return counter;
        }
    }
}
