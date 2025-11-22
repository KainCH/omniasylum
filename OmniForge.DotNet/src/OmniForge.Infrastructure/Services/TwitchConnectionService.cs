using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Twitch Connection Service starting...");

            // Wait for app startup to complete
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            await ConnectAllUsersAsync(stoppingToken);
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

                        if (user.IsActive && !string.IsNullOrEmpty(user.AccessToken))
                        {
                            _logger.LogInformation("Connecting Twitch bot for user: {Username}", user.Username);
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
