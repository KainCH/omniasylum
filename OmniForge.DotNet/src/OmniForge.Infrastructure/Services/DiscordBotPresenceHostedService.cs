using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class DiscordBotPresenceHostedService : IHostedService
    {
        private const string DefaultActivityText = "shaping commands in the forge";

        private readonly ILogger<DiscordBotPresenceHostedService> _logger;
        private readonly IDiscordBotClient _discordBotClient;
        private readonly DiscordBotSettings _settings;

        public DiscordBotPresenceHostedService(
            ILogger<DiscordBotPresenceHostedService> logger,
            IDiscordBotClient discordBotClient,
            IOptions<DiscordBotSettings> settings)
        {
            _logger = logger;
            _discordBotClient = discordBotClient;
            _settings = settings.Value;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.BotToken))
            {
                _logger.LogInformation("Discord bot presence disabled (no BotToken configured)");
                return;
            }

            try
            {
                _logger.LogInformation("🔄 Starting Discord bot presence (activity: {Activity})", LogSanitizer.Sanitize(DefaultActivityText));
                await _discordBotClient.EnsureOnlineAsync(_settings.BotToken, DefaultActivityText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to start Discord bot presence");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // DiscordNetBotClient is registered as a singleton and will be disposed by the host.
            return Task.CompletedTask;
        }
    }
}
