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
            var snapshotJson = entity.GetString("Snapshot");
            var snapshot = string.IsNullOrEmpty(snapshotJson)
                ? new Counter()
                : JsonSerializer.Deserialize<Counter>(snapshotJson) ?? new Counter();

            return new Series
            {
                UserId = entity.PartitionKey,
                Id = entity.RowKey,
                Name = entity.GetString("Name") ?? string.Empty,
                Description = entity.GetString("Description") ?? string.Empty,
                Snapshot = snapshot,
                CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.UtcNow,
                LastUpdated = entity.GetDateTimeOffset("LastUpdated") ?? DateTimeOffset.UtcNow,
                IsActive = entity.GetBoolean("IsActive") ?? false
            };
        }

        private TableEntity MapToEntity(Series series)
        {
            return new TableEntity(series.UserId, series.Id)
            {
                { "Name", series.Name },
                { "Description", series.Description },
                { "Snapshot", JsonSerializer.Serialize(series.Snapshot) },
                { "CreatedAt", series.CreatedAt },
                { "LastUpdated", series.LastUpdated },
                { "IsActive", series.IsActive }
            };
        }
    }
}
