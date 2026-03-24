using System;
using System.Collections.Generic;
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
    public class SceneRepository : ISceneRepository
    {
        private readonly TableClient _tableClient;

        public SceneRepository(TableServiceClient tableServiceClient, IOptions<AzureTableConfiguration> tableConfig)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.ScenesTable);
        }

        public async Task InitializeAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<List<Scene>> GetScenesAsync(string userId)
        {
            var scenes = new List<Scene>();
            await foreach (var entity in _tableClient.QueryAsync<SceneTableEntity>(filter: $"PartitionKey eq '{userId}'"))
            {
                scenes.Add(ToDomain(entity));
            }
            return scenes;
        }

        public async Task<Scene?> GetSceneAsync(string userId, string sceneName)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<SceneTableEntity>(userId, EscapeRowKey(sceneName));
                return ToDomain(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SaveSceneAsync(Scene scene)
        {
            var entity = new SceneTableEntity
            {
                PartitionKey = scene.UserId,
                RowKey = EscapeRowKey(scene.Name),
                SceneName = scene.Name,
                Source = scene.Source,
                FirstSeen = scene.FirstSeen,
                LastSeen = scene.LastSeen
            };
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        public async Task DeleteSceneAsync(string userId, string sceneName)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(userId, EscapeRowKey(sceneName));
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Already deleted
            }
        }

        private static Scene ToDomain(SceneTableEntity entity)
        {
            return new Scene
            {
                UserId = entity.PartitionKey,
                Name = entity.SceneName,
                Source = entity.Source,
                FirstSeen = entity.FirstSeen,
                LastSeen = entity.LastSeen
            };
        }

        internal static string EscapeRowKey(string name)
        {
            // Azure Table RowKey cannot contain: / \ # ?
            return name
                .Replace("/", "_SLASH_")
                .Replace("\\", "_BSLASH_")
                .Replace("#", "_HASH_")
                .Replace("?", "_QMARK_");
        }

        internal static string UnescapeRowKey(string rowKey)
        {
            return rowKey
                .Replace("_SLASH_", "/")
                .Replace("_BSLASH_", "\\")
                .Replace("_HASH_", "#")
                .Replace("_QMARK_", "?");
        }
    }
}
