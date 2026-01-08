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
                // NOTE: We intentionally read as TableEntity so we can support both legacy camelCase
                // (username/accessToken/refreshToken/tokenExpiry) and newer PascalCase properties.
                var response = await _tableClient.GetEntityAsync<TableEntity>(
                    BotCredentialsTableEntity.Partition,
                    BotCredentialsTableEntity.Row);

                var entity = response.Value;

                return new BotCredentials
                {
                    Username = GetString(entity, "Username", "username"),
                    AccessToken = GetString(entity, "AccessToken", "accessToken"),
                    RefreshToken = GetString(entity, "RefreshToken", "refreshToken"),
                    TokenExpiry = GetDateTimeOffset(entity, "TokenExpiry", "tokenExpiry")
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
                var safeExpiry = credentials.TokenExpiry < minAzureDate ? minAzureDate : credentials.TokenExpiry;

                // Write both naming styles so rolling deployments don't break.
                var entity = new TableEntity(BotCredentialsTableEntity.Partition, BotCredentialsTableEntity.Row)
                {
                    ["Username"] = credentials.Username,
                    ["username"] = credentials.Username,
                    ["AccessToken"] = credentials.AccessToken,
                    ["accessToken"] = credentials.AccessToken,
                    ["RefreshToken"] = credentials.RefreshToken,
                    ["refreshToken"] = credentials.RefreshToken,
                    ["TokenExpiry"] = safeExpiry,
                    ["tokenExpiry"] = safeExpiry
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

        private static string GetString(TableEntity entity, string key1, string key2)
        {
            if (entity.TryGetValue(key1, out var value1) && value1 != null)
            {
                return value1 as string ?? value1.ToString() ?? string.Empty;
            }

            if (entity.TryGetValue(key2, out var value2) && value2 != null)
            {
                return value2 as string ?? value2.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static DateTimeOffset GetDateTimeOffset(TableEntity entity, string key1, string key2)
        {
            if (TryGetDateTimeOffset(entity, key1, out var value) || TryGetDateTimeOffset(entity, key2, out value))
            {
                return value;
            }

            return DateTimeOffset.MinValue;
        }

        private static bool TryGetDateTimeOffset(TableEntity entity, string key, out DateTimeOffset value)
        {
            value = default;

            if (!entity.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            if (raw is DateTimeOffset dto)
            {
                value = dto;
                return true;
            }

            if (raw is DateTime dt)
            {
                value = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                return true;
            }

            if (raw is string s && DateTimeOffset.TryParse(s, out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }
    }
}
