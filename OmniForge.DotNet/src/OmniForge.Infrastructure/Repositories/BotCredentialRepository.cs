using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Entities;

namespace OmniForge.Infrastructure.Repositories
{
    public class BotCredentialRepository : IBotCredentialRepository
    {
        private readonly TableClient _tableClient;
        private readonly ILogger<BotCredentialRepository> _logger;

        public BotCredentialRepository(
            TableServiceClient tableServiceClient,
            IOptions<AzureTableConfiguration> tableConfig,
            ILogger<BotCredentialRepository> logger)
        {
            _tableClient = tableServiceClient.GetTableClient(tableConfig.Value.UsersTable);
            _logger = logger;
        }

        public async Task<BotCredentials?> GetAsync()
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<BotCredentialsTableEntity>(
                    BotCredentialsTableEntity.Partition,
                    BotCredentialsTableEntity.Row);

                var entity = response.Value;
                return new BotCredentials
                {
                    Username = entity.username,
                    AccessToken = entity.accessToken,
                    RefreshToken = entity.refreshToken,
                    TokenExpiry = entity.tokenExpiry
                };
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting Forge bot credentials from Azure Table Storage");
                throw;
            }
        }

        public async Task SaveAsync(BotCredentials credentials)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(credentials.Username))
                {
                    throw new ArgumentException("Bot username is required", nameof(credentials));
                }

                var minAzureDate = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);

                var entity = new BotCredentialsTableEntity
                {
                    username = credentials.Username,
                    accessToken = credentials.AccessToken,
                    refreshToken = credentials.RefreshToken,
                    tokenExpiry = credentials.TokenExpiry < minAzureDate ? minAzureDate : credentials.TokenExpiry
                };

                await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
                _logger.LogInformation("✅ Saved Forge bot credentials for {Username}", LogSanitizer.Sanitize(credentials.Username));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving Forge bot credentials");
                throw;
            }
        }
    }
}
