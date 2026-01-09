using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services
{
    public class TwitchConnectionService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ITwitchClientManager _twitchClientManager;
        private readonly ILogger<TwitchConnectionService> _logger;

        public TwitchConnectionService(
            IServiceProvider serviceProvider,
            ITwitchClientManager twitchClientManager,
            ILogger<TwitchConnectionService> logger)
        {
            _serviceProvider = serviceProvider;
            _twitchClientManager = twitchClientManager;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Twitch connections are now user-initiated via "Start Monitor" button
            // Auto-connect on startup is disabled to respect user preferences and avoid
            // unnecessary authentication attempts with potentially expired tokens
            _logger.LogInformation("Twitch Connection Service initialized (connections are user-initiated via Start Monitor)");
            return Task.CompletedTask;
        }

        public async Task ConnectAllUsersAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                    var users = await userRepository.GetAllUsersAsync();

                    foreach (var user in users)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        if (user.IsActive && !string.IsNullOrEmpty(user.Username))
                        {
                            _logger.LogInformation("Connecting Twitch bot for user: {Username}", LogSanitizer.Sanitize(user.Username));
                            await _twitchClientManager.ConnectUserAsync(user.TwitchUserId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Twitch connections");
            }
        }
    }
}
