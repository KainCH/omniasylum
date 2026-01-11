using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class CounterLibraryRepository : ICounterLibraryRepository
    {
        private readonly TableClient _tableClient;

        public CounterLibraryRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.CounterLibraryTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<IEnumerable<CounterLibraryItem>> ListAsync()
        {
            var results = new List<CounterLibraryItem>();
            var query = _tableClient.QueryAsync<CounterLibraryTableEntity>(filter: "PartitionKey eq 'counter'");

            await foreach (var entity in query)
            {
                var milestones = ParseMilestones(entity.MilestonesJson);
                results.Add(entity.ToItem(milestones));
            }

            return results.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<CounterLibraryItem?> GetAsync(string counterId)
        {
            if (string.IsNullOrWhiteSpace(counterId)) return null;

            try
            {
                var response = await _tableClient.GetEntityAsync<CounterLibraryTableEntity>("counter", counterId);
                var entity = response.Value;
                return entity.ToItem(ParseMilestones(entity.MilestonesJson));
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task UpsertAsync(CounterLibraryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.CounterId)) throw new ArgumentException("CounterId is required", nameof(item));

            var milestonesJson = JsonSerializer.Serialize(item.Milestones ?? Array.Empty<int>());
            var entity = CounterLibraryTableEntity.FromItem(item, milestonesJson);
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        private static int[] ParseMilestones(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<int>();

            try
            {
                return JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>();
            }
            catch
            {
                return Array.Empty<int>();
            }
        }
    }
}
