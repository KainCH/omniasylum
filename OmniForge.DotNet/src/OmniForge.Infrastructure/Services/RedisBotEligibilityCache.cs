using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using StackExchange.Redis;

namespace OmniForge.Infrastructure.Services
{
    public class RedisBotEligibilityCache : IBotEligibilityCache, IDisposable
    {
        private const string KeyPrefix = "botEligibility:v1";

        private readonly RedisSettings _settings;
        private readonly ILogger<RedisBotEligibilityCache> _logger;
        private readonly Lazy<Task<IConnectionMultiplexer?>> _connection;
        private readonly Func<Task<IDatabase?>>? _databaseFactory;

        private sealed record CacheEntry(bool UseBot, string? BotUserId, string? Reason);

        public RedisBotEligibilityCache(
            IOptions<RedisSettings> settings,
            ILogger<RedisBotEligibilityCache> logger,
            Func<Task<IConnectionMultiplexer?>>? connectionFactory = null,
            Func<Task<IDatabase?>>? databaseFactory = null)
        {
            _settings = settings.Value;
            _logger = logger;
            _databaseFactory = databaseFactory;

            _connection = new Lazy<Task<IConnectionMultiplexer?>>(() => (connectionFactory ?? ConnectAsync)());
        }

        public async Task<BotEligibilityResult?> TryGetAsync(string broadcasterUserId, string botLoginOrId, CancellationToken cancellationToken = default)
        {
            try
            {
                var db = await GetDbAsync().ConfigureAwait(false);
                if (db == null)
                {
                    return null;
                }

                var key = BuildKey(broadcasterUserId, botLoginOrId);
                var value = await db.StringGetAsync(key).ConfigureAwait(false);
                if (value.IsNullOrEmpty)
                {
                    return null;
                }

                var entry = JsonSerializer.Deserialize<CacheEntry>(value!);
                if (entry == null)
                {
                    return null;
                }

                return new BotEligibilityResult(entry.UseBot, entry.BotUserId, entry.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Redis eligibility cache read failed; treating as cache miss");
                return null;
            }
        }

        public async Task SetAsync(string broadcasterUserId, string botLoginOrId, BotEligibilityResult result, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            try
            {
                var db = await GetDbAsync().ConfigureAwait(false);
                if (db == null)
                {
                    return;
                }

                var key = BuildKey(broadcasterUserId, botLoginOrId);
                var entry = new CacheEntry(result.UseBot, result.BotUserId, result.Reason);
                var json = JsonSerializer.Serialize(entry);

                await db.StringSetAsync(key, (RedisValue)json, ttl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Redis eligibility cache write failed; continuing without cache");
            }
        }

        private string BuildKey(string broadcasterUserId, string botLoginOrId)
        {
            var ns = string.IsNullOrWhiteSpace(_settings.KeyNamespace)
                ? "default"
                : _settings.KeyNamespace.Trim().ToLowerInvariant();

            var broadcaster = (broadcasterUserId ?? string.Empty).Trim();
            var bot = (botLoginOrId ?? string.Empty).Trim().ToLowerInvariant();
            return $"{KeyPrefix}:{ns}:{broadcaster}:{bot}";
        }

        private async Task<IDatabase?> GetDbAsync()
        {
            if (_databaseFactory != null)
            {
                return await _databaseFactory().ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(_settings.HostName))
            {
                return null;
            }

            var mux = await _connection.Value.ConfigureAwait(false);
            if (mux == null)
            {
                return null;
            }

            // If Redis isn't connected, skip caching entirely to avoid per-operation timeouts.
            if (!mux.IsConnected)
            {
                return null;
            }

            return mux.GetDatabase();
        }

        private async Task<IConnectionMultiplexer?> ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(_settings.HostName))
            {
                return null;
            }

            try
            {
                // Entra/Managed Identity auth: token is acquired via DefaultAzureCredential.
                // HostName should include port, e.g. "omniforge.redis.cache.windows.net:6380".
                var credential = new DefaultAzureCredential();

                var options = ConfigurationOptions.Parse(_settings.HostName);
                await AzureCacheForRedis.ConfigureForAzureWithTokenCredentialAsync(options, credential).ConfigureAwait(false);

                options.AbortOnConnectFail = false;

                var mux = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
                try
                {
                    // Proactively validate connectivity; if this fails, treat Redis as unavailable.
                    await mux.GetDatabase().PingAsync().ConfigureAwait(false);
                    _logger.LogInformation("✅ Connected to Redis for caching. host={Host}", LogSanitizer.Sanitize(_settings.HostName));
                    return mux;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Redis connected but not ready; disabling cache. host={Host}", LogSanitizer.Sanitize(_settings.HostName));
                    try { mux.Dispose(); } catch { }
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to connect to Redis. host={Host}", LogSanitizer.Sanitize(_settings.HostName));
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_connection.IsValueCreated)
                {
                    var connectTask = _connection.Value;
                    if (connectTask.IsCompletedSuccessfully)
                    {
                        connectTask.Result?.Dispose();
                    }
                    else
                    {
                        _ = connectTask.ContinueWith(t =>
                        {
                            try
                            {
                                if (t.Status == TaskStatus.RanToCompletion)
                                {
                                    t.Result?.Dispose();
                                }
                            }
                            catch
                            {
                                // Ignore dispose failures
                            }
                        }, TaskScheduler.Default);
                    }
                }
            }
            catch
            {
                // Ignore dispose failures
            }
        }
    }
}
