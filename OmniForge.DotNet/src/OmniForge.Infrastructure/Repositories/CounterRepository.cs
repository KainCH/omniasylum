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
                var response = await _tableClient.GetEntityAsync<TableEntity>(twitchUserId, "counters");
                var entity = response.Value;

                var counter = new Counter
                {
                    TwitchUserId = entity.PartitionKey,
                    Deaths = entity.GetInt32("Deaths") ?? 0,
                    Swears = entity.GetInt32("Swears") ?? 0,
                    Screams = entity.GetInt32("Screams") ?? 0,
                    Bits = entity.GetInt32("Bits") ?? 0,
                    LastUpdated = entity.GetDateTimeOffset("LastUpdated") ?? DateTimeOffset.UtcNow,
                    StreamStarted = entity.GetDateTimeOffset("StreamStarted"),
                    LastNotifiedStreamId = entity.GetString("LastNotifiedStreamId")
                };

                // Map custom counters
                foreach (var key in entity.Keys)
                {
                    if (key != "PartitionKey" && key != "RowKey" && key != "Timestamp" && key != "odata.etag" &&
                        key != "Deaths" && key != "Swears" && key != "Screams" && key != "Bits" &&
                        key != "LastUpdated" && key != "StreamStarted" && key != "LastNotifiedStreamId")
                    {
                        if (entity[key] is int value)
                        {
                            counter.CustomCounters[key] = value;
                        }
                        else if (entity[key] is long longValue)
                        {
                             counter.CustomCounters[key] = (int)longValue;
                        }
                    }
                }

                return counter;
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
            var entity = new TableEntity(counter.TwitchUserId, "counters")
            {
                ["Deaths"] = counter.Deaths,
                ["Swears"] = counter.Swears,
                ["Screams"] = counter.Screams,
                ["Bits"] = counter.Bits,
                ["LastUpdated"] = counter.LastUpdated,
                ["StreamStarted"] = counter.StreamStarted,
                ["LastNotifiedStreamId"] = counter.LastNotifiedStreamId
            };

            foreach (var kvp in counter.CustomCounters)
            {
                entity[kvp.Key] = kvp.Value;
            }

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task<Counter> IncrementCounterAsync(string userId, string counterType, int amount = 1)
        {
            var counter = await GetCountersAsync(userId);

            switch (counterType.ToLower())
            {
                case "deaths":
                    counter.Deaths += amount;
                    break;
                case "swears":
                    counter.Swears += amount;
                    break;
                case "screams":
                    counter.Screams += amount;
                    break;
                default:
                    if (counter.CustomCounters.ContainsKey(counterType))
                    {
                        counter.CustomCounters[counterType] += amount;
                    }
                    else
                    {
                        counter.CustomCounters[counterType] = amount;
                    }
                    break;
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
                    if (counter.CustomCounters.ContainsKey(counterType))
                    {
                        counter.CustomCounters[counterType] = Math.Max(0, counter.CustomCounters[counterType] - 1);
                    }
                    break;
            }

            counter.LastUpdated = DateTimeOffset.UtcNow;
            await SaveCountersAsync(counter);
            return counter;
        }

        public async Task<CustomCounterConfiguration> GetCustomCountersConfigAsync(string userId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<CustomCounterConfigTableEntity>(userId, "customCounters");
                return response.Value.ToConfiguration();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return new CustomCounterConfiguration();
            }
        }

        public async Task SaveCustomCountersConfigAsync(string userId, CustomCounterConfiguration config)
        {
            var entity = CustomCounterConfigTableEntity.FromConfiguration(userId, config);
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }
    }
}
