using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles stream.offline EventSub notifications.
    /// </summary>
    public class StreamOfflineHandler : BaseEventSubHandler
    {
        private readonly IDiscordInviteBroadcastScheduler _discordInviteBroadcastScheduler;

        public StreamOfflineHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<StreamOfflineHandler> logger,
            IDiscordInviteBroadcastScheduler discordInviteBroadcastScheduler)
            : base(scopeFactory, logger)
        {
            _discordInviteBroadcastScheduler = discordInviteBroadcastScheduler;
        }

        public override string SubscriptionType => "stream.offline";

        public override async Task HandleAsync(JsonElement eventData)
        {
            string? broadcasterId = GetStringProperty(eventData, "broadcaster_user_id");
            string broadcasterName = GetStringProperty(eventData, "broadcaster_user_name", "Unknown");

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return;
            }

            Logger.LogInformation("Stream Offline: {BroadcasterName} ({BroadcasterId})", broadcasterName, broadcasterId);

            // Stop periodic Discord invite broadcasting.
            await _discordInviteBroadcastScheduler.StopAsync(broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            var user = await userRepository.GetUserAsync(broadcasterId);
            if (user == null)
            {
                return;
            }

            // Update Counter
            var counters = await counterRepository.GetCountersAsync(broadcasterId);
            if (counters != null)
            {
                counters.StreamStarted = null;
                await counterRepository.SaveCountersAsync(counters);
            }

            // Notify Overlay
            if (overlayNotifier != null && counters != null)
            {
                await overlayNotifier.NotifyStreamEndedAsync(broadcasterId, counters);
            }
        }
    }
}
