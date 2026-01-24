using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class CounterRepository : ICounterRepository
    {
        private readonly TableClient _tableClient;

        public CounterRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.CountersTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<Counter?> GetCountersAsync(string twitchUserId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(twitchUserId, "counters");
                var entity = response.Value;

                var counter = new Counter
                {
                    TwitchUserId = entity.PartitionKey,
                    Deaths = GetInt32SafeCaseInsensitive(entity, "Deaths"),
                    Swears = GetInt32SafeCaseInsensitive(entity, "Swears"),
                    Screams = GetInt32SafeCaseInsensitive(entity, "Screams"),
                    Bits = GetInt32SafeCaseInsensitive(entity, "Bits"),
                    LastUpdated = GetDateTimeOffsetSafe(entity, "LastUpdated") ?? GetDateTimeOffsetSafe(entity, "lastUpdated") ?? DateTimeOffset.UtcNow,
                    StreamStarted = GetDateTimeOffsetSafe(entity, "StreamStarted") ?? GetDateTimeOffsetSafe(entity, "streamStarted"),
                    LastNotifiedStreamId = entity.GetString("LastNotifiedStreamId") ?? entity.GetString("lastNotifiedStreamId"),
                    LastCategoryName = entity.GetString("LastCategoryName") ?? entity.GetString("lastCategoryName")
                };

                // Map custom counters
                foreach (var key in entity.Keys)
                {
                    if (key != "PartitionKey" && key != "RowKey" && key != "Timestamp" && key != "odata.etag" &&
                        !string.Equals(key, "Deaths", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "Swears", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "Screams", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "Bits", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "LastUpdated", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "StreamStarted", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "LastNotifiedStreamId", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "LastCategoryName", StringComparison.OrdinalIgnoreCase))
                    {
                        // Handle various numeric types that may be stored
                        // Use nullable pattern to properly distinguish "not found" from valid values
                        if (TryGetInt32Safe(entity, key, out var customValue))
                        {
                            counter.CustomCounters[key] = customValue;
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
                ["LastNotifiedStreamId"] = counter.LastNotifiedStreamId,
                ["LastCategoryName"] = counter.LastCategoryName
            };

            foreach (var kvp in counter.CustomCounters)
            {
                entity[kvp.Key] = kvp.Value;
            }

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task<bool> TryClaimStreamStartDiscordNotificationAsync(string userId, string streamInstanceId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(streamInstanceId))
            {
                return false;
            }

            // This is an idempotency guard used to suppress duplicate Discord "stream_start" announcements.
            // Uses optimistic concurrency (ETag) so multiple app instances will not double-send.
            const int maxAttempts = 5;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var response = await _tableClient.GetEntityAsync<TableEntity>(userId, "counters");
                    var existingEntity = response.Value;
                    var existing = existingEntity.GetString("LastNotifiedStreamId") ?? existingEntity.GetString("lastNotifiedStreamId");
                    if (string.Equals(existing, streamInstanceId, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var patch = new TableEntity(userId, "counters")
                    {
                        ["LastNotifiedStreamId"] = streamInstanceId,
                        // Backward compatibility: older builds used camelCase.
                        ["lastNotifiedStreamId"] = streamInstanceId,
                        ["LastUpdated"] = DateTimeOffset.UtcNow
                    };

                    await _tableClient.UpdateEntityAsync(patch, existingEntity.ETag, TableUpdateMode.Merge);
                    return true;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Entity doesn't exist yet; create a minimal one with the claim.
                    var newEntity = new TableEntity(userId, "counters")
                    {
                        ["Deaths"] = 0,
                        ["Swears"] = 0,
                        ["Screams"] = 0,
                        ["Bits"] = 0,
                        ["LastUpdated"] = DateTimeOffset.UtcNow,
                        ["LastNotifiedStreamId"] = streamInstanceId,
                        // Backward compatibility: older builds used camelCase.
                        ["lastNotifiedStreamId"] = streamInstanceId
                    };

                    try
                    {
                        await _tableClient.AddEntityAsync(newEntity);
                        return true;
                    }
                    catch (RequestFailedException addEx) when (addEx.Status == 409)
                    {
                        // Someone else created it; retry.
                    }
                }
                catch (RequestFailedException ex) when (ex.Status == 412)
                {
                    // ETag mismatch (concurrent update). Retry.
                }

                if (attempt < maxAttempts - 1)
                {
                    // Exponential backoff to reduce contention in multi-instance scenarios.
                    var delayMs = 50 * (1 << attempt);
                    await Task.Delay(delayMs);
                }
            }

            return false;
        }

        /// <summary>
        /// Gets an int value checking both PascalCase and camelCase key variations.
        /// This handles data that may have been stored with different casing conventions.
        /// </summary>
        private int GetInt32SafeCaseInsensitive(TableEntity entity, string pascalCaseKey, int defaultValue = 0)
        {
            // Try PascalCase first (preferred), then camelCase
            var camelCaseKey = char.ToLowerInvariant(pascalCaseKey[0]) + pascalCaseKey.Substring(1);

            if (TryGetInt32Safe(entity, pascalCaseKey, out var value))
            {
                return value;
            }
            if (TryGetInt32Safe(entity, camelCaseKey, out value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Tries to get an int value from the entity, handling various storage types.
        /// Returns true if a valid integer was found, false otherwise.
        /// This properly distinguishes between "not found" and valid values (including int.MinValue).
        /// </summary>
        private bool TryGetInt32Safe(TableEntity entity, string key, out int result)
        {
            result = 0;
            if (entity.TryGetValue(key, out var value))
            {
                if (value is int intValue)
                {
                    result = intValue;
                    return true;
                }
                if (value is long longValue)
                {
                    result = (int)longValue;
                    return true;
                }
                if (value is double doubleValue)
                {
                    result = (int)doubleValue;
                    return true;
                }
                if (value is string stringValue && int.TryParse(stringValue, out var parsedInt))
                {
                    result = parsedInt;
                    return true;
                }
            }
            return false;
        }

        private DateTimeOffset? GetDateTimeOffsetSafe(TableEntity entity, string key)
        {
            if (entity.TryGetValue(key, out var value))
            {
                if (value is DateTimeOffset dto)
                {
                    return dto;
                }
                if (value is DateTime dt)
                {
                    return new DateTimeOffset(dt);
                }
                if (value is string s && DateTimeOffset.TryParse(s, out var parsedDto))
                {
                    return parsedDto;
                }
            }
            return null;
        }

        public async Task<Counter> IncrementCounterAsync(string userId, string counterType, int amount = 1)
        {
            var counter = await GetCountersAsync(userId) ?? new Counter { TwitchUserId = userId, LastUpdated = DateTimeOffset.UtcNow };

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

        public async Task<Counter> DecrementCounterAsync(string userId, string counterType, int amount = 1)
        {
            var counter = await GetCountersAsync(userId) ?? new Counter { TwitchUserId = userId, LastUpdated = DateTimeOffset.UtcNow };

            switch (counterType.ToLower())
            {
                case "deaths":
                    counter.Deaths = Math.Max(0, counter.Deaths - amount);
                    break;
                case "swears":
                    counter.Swears = Math.Max(0, counter.Swears - amount);
                    break;
                case "screams":
                    counter.Screams = Math.Max(0, counter.Screams - amount);
                    break;
                default:
                    if (counter.CustomCounters.ContainsKey(counterType))
                    {
                        counter.CustomCounters[counterType] = Math.Max(0, counter.CustomCounters[counterType] - amount);
                    }
                    break;
            }

            counter.LastUpdated = DateTimeOffset.UtcNow;
            await SaveCountersAsync(counter);
            return counter;
        }

        public async Task<Counter> ResetCounterAsync(string userId, string counterType)
        {
            var counter = await GetCountersAsync(userId) ?? new Counter { TwitchUserId = userId, LastUpdated = DateTimeOffset.UtcNow };

            switch (counterType.ToLower())
            {
                case "deaths":
                    counter.Deaths = 0;
                    break;
                case "swears":
                    counter.Swears = 0;
                    break;
                case "screams":
                    counter.Screams = 0;
                    break;
                default:
                    if (counter.CustomCounters.ContainsKey(counterType))
                    {
                        counter.CustomCounters[counterType] = 0;
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
