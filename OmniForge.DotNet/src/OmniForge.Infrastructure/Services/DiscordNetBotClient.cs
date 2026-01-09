using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class DiscordNetBotClient : IDiscordBotClient, IDisposable
    {
        private readonly ILogger<DiscordNetBotClient> _logger;
        private readonly SemaphoreSlim _clientLock = new SemaphoreSlim(1, 1);

        private DiscordRestClient? _client;
        private string? _botToken;

        private DiscordSocketClient? _gatewayClient;
        private string? _gatewayToken;
        private string? _gatewayActivity;

        private Task? _gatewayStartTask;

        public DiscordNetBotClient(ILogger<DiscordNetBotClient> logger)
        {
            _logger = logger;
        }

        public async Task EnsureOnlineAsync(string botToken, string activityText)
        {
            if (string.IsNullOrWhiteSpace(botToken)) return;
            if (string.IsNullOrWhiteSpace(activityText)) activityText = "shaping commands in the forge";

            await EnsureGatewayConnectedAsync(botToken, activityText, UserStatus.Online);
        }

        public async Task SetIdleAsync(string botToken, string activityText)
        {
            if (string.IsNullOrWhiteSpace(botToken)) return;
            if (string.IsNullOrWhiteSpace(activityText)) activityText = "shaping commands in the forge";

            await EnsureGatewayConnectedAsync(botToken, activityText, UserStatus.Idle);
        }

        public async Task<bool> ValidateChannelAsync(string channelId, string botToken)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return false;
            if (!ulong.TryParse(channelId, out var channelSnowflake)) return false;
            if (string.IsNullOrWhiteSpace(botToken)) return false;

            try
            {
                // Best effort: keep the bot "online" when configured.
                FireAndForget(EnsureGatewayConnectedAsync(botToken, "shaping commands in the forge", UserStatus.Online), "EnsureGatewayConnectedAsync(ValidateChannelAsync)");

                var client = await GetClientAsync(botToken).ConfigureAwait(false);
                var channel = await client.GetChannelAsync(channelSnowflake).ConfigureAwait(false);
                return channel != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating Discord channel ID");
                return false;
            }
        }

        public async Task SendMessageAsync(string channelId, string botToken, string? content, Embed embed, MessageComponent? components, AllowedMentions allowedMentions)
        {
            if (string.IsNullOrWhiteSpace(channelId)) throw new ArgumentException("Channel ID is required", nameof(channelId));
            if (!ulong.TryParse(channelId, out var channelSnowflake)) throw new ArgumentException("Channel ID must be a valid snowflake", nameof(channelId));
            if (string.IsNullOrWhiteSpace(botToken)) throw new ArgumentException("Bot token is required", nameof(botToken));

            // Best effort: keep the bot "online" when configured.
            FireAndForget(EnsureGatewayConnectedAsync(botToken, "shaping commands in the forge", UserStatus.Online), "EnsureGatewayConnectedAsync(SendMessageAsync)");

            var client = await GetClientAsync(botToken).ConfigureAwait(false);

            var channel = await client.GetChannelAsync(channelSnowflake).ConfigureAwait(false);
            if (channel is not IMessageChannel messageChannel)
            {
                throw new InvalidOperationException("Discord channel is not a message channel or was not found");
            }

            _logger.LogInformation("Sending Discord bot message to channelId={ChannelId}", LogSanitizer.Sanitize(channelId));

            await messageChannel.SendMessageAsync(
                text: content,
                embed: embed,
                allowedMentions: allowedMentions,
                components: components);

            _logger.LogInformation("Discord bot message sent successfully");
        }

        private async Task<DiscordRestClient> GetClientAsync(string botToken)
        {
            await _clientLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_client != null && string.Equals(_botToken, botToken, StringComparison.Ordinal))
                {
                    return _client;
                }

                try
                {
                    _client?.Dispose();
                }
                catch
                {
                    // Ignore dispose failures
                }

                var client = new DiscordRestClient();
                await client.LoginAsync(TokenType.Bot, botToken).ConfigureAwait(false);

                _client = client;
                _botToken = botToken;

                return client;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        private async Task EnsureGatewayConnectedAsync(string botToken, string activityText, UserStatus desiredStatus)
        {
            DiscordSocketClient? clientToStart = null;
            bool needsStart = false;
            Task? startTaskToAwait = null;
            DiscordSocketClient? presenceClientToUpdate = null;
            bool shouldReturnAfterLock = false;

            await _clientLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _gatewayActivity = activityText;

                // If a start is already in progress, just wait for it rather than racing.
                if (_gatewayStartTask != null && !_gatewayStartTask.IsCompleted)
                {
                    startTaskToAwait = _gatewayStartTask;
                    // Wait for the in-flight start outside the lock.
                }

                var gatewayClient = _gatewayClient;
                if (gatewayClient != null && string.Equals(_gatewayToken, botToken, StringComparison.Ordinal))
                {
                    // If we're already connected or connecting, nothing to do.
                    if (gatewayClient.ConnectionState == ConnectionState.Connected || gatewayClient.ConnectionState == ConnectionState.Connecting)
                    {
                        if (gatewayClient.ConnectionState == ConnectionState.Connected)
                        {
                            // Run presence update outside the lock.
                            presenceClientToUpdate = gatewayClient;
                        }

                        shouldReturnAfterLock = true;
                    }

                    // If it's disconnected, attempt a restart.
                    clientToStart = gatewayClient;
                    needsStart = true;
                    // fall through to start outside lock
                }
                else
                {
                    try
                    {
                        _gatewayClient?.Dispose();
                    }
                    catch
                    {
                        // Ignore dispose failures
                    }

                    var socketClient = new DiscordSocketClient(new DiscordSocketConfig
                    {
                        GatewayIntents = GatewayIntents.Guilds,
                        AlwaysDownloadUsers = false
                    });

                    socketClient.Log += msg =>
                    {
                        try
                        {
                            if (msg.Exception != null)
                            {
                                _logger.LogWarning(msg.Exception, "Discord gateway: {Message}", msg.Message);
                            }
                            else
                            {
                                _logger.LogInformation("Discord gateway: {Message}", msg.Message);
                            }
                        }
                        catch
                        {
                            // Ignore logging failures
                        }

                        return Task.CompletedTask;
                    };

                    socketClient.Ready += async () =>
                    {
                        try
                        {
                            var activity = string.IsNullOrWhiteSpace(_gatewayActivity) ? "shaping commands in the forge" : _gatewayActivity;
                            await socketClient.SetStatusAsync(desiredStatus);
                            await socketClient.SetGameAsync(activity);
                            _logger.LogInformation("✅ Discord bot is online with activity: {Activity}", LogSanitizer.Sanitize(activity));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to set Discord bot presence/activity");
                        }
                    };

                    _gatewayClient = socketClient;
                    _gatewayToken = botToken;
                    clientToStart = socketClient;
                    needsStart = true;
                }

                if (needsStart && clientToStart != null)
                {
                    // Single-flight gateway start
                    startTaskToAwait = StartGatewayAsync(clientToStart, botToken, desiredStatus);
                    _gatewayStartTask = startTaskToAwait;
                }
            }
            finally
            {
                _clientLock.Release();
            }

            if (shouldReturnAfterLock)
            {
                if (presenceClientToUpdate != null)
                {
                    await UpdatePresenceAsync(presenceClientToUpdate, desiredStatus).ConfigureAwait(false);
                }
                return;
            }

            if (startTaskToAwait != null)
            {
                try
                {
                    await startTaskToAwait.ConfigureAwait(false);
                }
                finally
                {
                    await _clientLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (_gatewayStartTask != null && _gatewayStartTask.IsCompleted)
                        {
                            _gatewayStartTask = null;
                        }
                    }
                    finally
                    {
                        _clientLock.Release();
                    }
                }
            }
        }

        private async Task StartGatewayAsync(DiscordSocketClient clientToStart, string botToken, UserStatus desiredStatus)
        {
            try
            {
                if (clientToStart.LoginState != LoginState.LoggedIn)
                {
                    await clientToStart.LoginAsync(TokenType.Bot, botToken).ConfigureAwait(false);
                }

                if (clientToStart.ConnectionState != ConnectionState.Connected && clientToStart.ConnectionState != ConnectionState.Connecting)
                {
                    await clientToStart.StartAsync().ConfigureAwait(false);
                }

                if (clientToStart.ConnectionState == ConnectionState.Connected)
                {
                    await UpdatePresenceAsync(clientToStart, desiredStatus).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to connect Discord bot gateway client");
            }
        }

        private async Task UpdatePresenceAsync(DiscordSocketClient socketClient, UserStatus desiredStatus)
        {
            try
            {
                var activity = string.IsNullOrWhiteSpace(_gatewayActivity) ? "shaping commands in the forge" : _gatewayActivity;
                await socketClient.SetStatusAsync(desiredStatus).ConfigureAwait(false);
                await socketClient.SetGameAsync(activity).ConfigureAwait(false);

                _logger.LogInformation(
                    "✅ Discord bot presence updated: Status={Status}, Activity={Activity}",
                    desiredStatus,
                    LogSanitizer.Sanitize(activity));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Discord bot presence/activity");
            }
        }

        public void Dispose()
        {
            var restClient = Interlocked.Exchange(ref _client, null);
            Interlocked.Exchange(ref _botToken, null);

            var gatewayClient = Interlocked.Exchange(ref _gatewayClient, null);
            Interlocked.Exchange(ref _gatewayToken, null);
            Interlocked.Exchange(ref _gatewayActivity, null);
            Interlocked.Exchange(ref _gatewayStartTask, null);

            try
            {
                restClient?.Dispose();
            }
            catch
            {
                // Ignore dispose failures
            }

            try
            {
                if (gatewayClient != null)
                {
                    try
                    {
                        gatewayClient.StopAsync().Wait(TimeSpan.FromSeconds(2));
                    }
                    catch
                    {
                        // Ignore stop failures
                    }

                    try
                    {
                        gatewayClient.Dispose();
                    }
                    catch
                    {
                        // Ignore dispose failures
                    }
                }
            }
            catch
            {
                // Ignore dispose failures
            }
        }

        private void FireAndForget(Task task, string operation)
        {
            if (task.IsCompleted)
            {
                return;
            }

            _ = task.ContinueWith(
                t => _logger.LogError(t.Exception, "❌ Fire-and-forget operation failed: {Operation}", operation),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
