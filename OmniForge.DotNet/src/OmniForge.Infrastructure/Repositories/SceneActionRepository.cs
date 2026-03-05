using System;
using System.Collections.Generic;
using System.Text.Json;
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
    public class SceneActionRepository : ISceneActionRepository
    {
        private readonly TableClient _tableClient;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public SceneActionRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.SceneActionsTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<SceneAction?> GetAsync(string userId, string sceneName)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<SceneActionTableEntity>(userId, SceneRepository.EscapeRowKey(sceneName));
                return ToDomain(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<List<SceneAction>> GetAllAsync(string userId)
        {
            var actions = new List<SceneAction>();
            await foreach (var entity in _tableClient.QueryAsync<SceneActionTableEntity>(e => e.PartitionKey == userId))
            {
                actions.Add(ToDomain(entity));
            }
            return actions;
        }

        public async Task SaveAsync(SceneAction sceneAction)
        {
            var entity = new SceneActionTableEntity
            {
                PartitionKey = sceneAction.UserId,
                RowKey = SceneRepository.EscapeRowKey(sceneAction.SceneName),
                ActionJson = JsonSerializer.Serialize(sceneAction, _jsonOptions),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task DeleteAsync(string userId, string sceneName)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(userId, SceneRepository.EscapeRowKey(sceneName));
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Already deleted
            }
        }

        private static SceneAction ToDomain(SceneActionTableEntity entity)
        {
            try
            {
                var action = JsonSerializer.Deserialize<SceneAction>(entity.ActionJson, _jsonOptions) ?? new SceneAction();
                action.UserId = entity.PartitionKey;
                return action;
            }
            catch
            {
                return new SceneAction { UserId = entity.PartitionKey, SceneName = entity.RowKey };
            }
        }
    }
}
