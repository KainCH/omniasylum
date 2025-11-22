using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System.Text.Json;

namespace OmniForge.Infrastructure.Repositories
{
    public class SeriesRepository : ISeriesRepository
    {
        private readonly TableClient _tableClient;

        public SeriesRepository(TableServiceClient tableServiceClient)
        {
            _tableClient = tableServiceClient.GetTableClient("series");
            _tableClient.CreateIfNotExists();
        }

        public async Task<IEnumerable<Series>> GetSeriesAsync(string userId)
        {
            var query = _tableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{userId}'");
            var seriesList = new List<Series>();

            await foreach (var entity in query)
            {
                seriesList.Add(MapToSeries(entity));
            }

            return seriesList.OrderByDescending(s => s.LastUpdated);
        }

        public async Task<Series?> GetSeriesByIdAsync(string userId, string seriesId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(userId, seriesId);
                return MapToSeries(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task CreateSeriesAsync(Series series)
        {
            var entity = MapToEntity(series);
            await _tableClient.AddEntityAsync(entity);
        }

        public async Task UpdateSeriesAsync(Series series)
        {
            var entity = MapToEntity(series);
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task DeleteSeriesAsync(string userId, string seriesId)
        {
            await _tableClient.DeleteEntityAsync(userId, seriesId);
        }

        private Series MapToSeries(TableEntity entity)
        {
            var snapshotJson = entity.GetString("snapshot");
            var snapshot = string.IsNullOrEmpty(snapshotJson)
                ? new Counter()
                : JsonSerializer.Deserialize<Counter>(snapshotJson) ?? new Counter();

            return new Series
            {
                UserId = entity.PartitionKey,
                Id = entity.RowKey,
                Name = entity.GetString("name") ?? string.Empty,
                Description = entity.GetString("description") ?? string.Empty,
                Snapshot = snapshot,
                CreatedAt = entity.GetDateTimeOffset("createdAt") ?? DateTimeOffset.UtcNow,
                LastUpdated = entity.GetDateTimeOffset("lastUpdated") ?? DateTimeOffset.UtcNow,
                IsActive = entity.GetBoolean("isActive") ?? false
            };
        }

        private TableEntity MapToEntity(Series series)
        {
            return new TableEntity(series.UserId, series.Id)
            {
                { "name", series.Name },
                { "description", series.Description },
                { "snapshot", JsonSerializer.Serialize(series.Snapshot) },
                { "createdAt", series.CreatedAt },
                { "lastUpdated", series.LastUpdated },
                { "isActive", series.IsActive }
            };
        }
    }
}
