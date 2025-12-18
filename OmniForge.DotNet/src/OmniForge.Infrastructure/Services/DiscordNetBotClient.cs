using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
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

        public DiscordNetBotClient(ILogger<DiscordNetBotClient> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ValidateChannelAsync(string channelId, string botToken)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return false;
            if (!ulong.TryParse(channelId, out var channelSnowflake)) return false;
            if (string.IsNullOrWhiteSpace(botToken)) return false;

            try
            {
                var client = await GetClientAsync(botToken);
                var channel = await client.GetChannelAsync(channelSnowflake);
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

            var client = await GetClientAsync(botToken);

            var channel = await client.GetChannelAsync(channelSnowflake);
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
            await _clientLock.WaitAsync();
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
                await client.LoginAsync(TokenType.Bot, botToken);

                _client = client;
                _botToken = botToken;

                return client;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public void Dispose()
        {
            try
            {
                _client?.Dispose();
            }
            catch
            {
                // Ignore dispose failures
            }
            _client = null;
            _botToken = null;
        }
    }
}
