using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles <c>channel.suspicious_user.message</c> EventSub notifications.
    /// When a broadcaster has <see cref="Core.Entities.FeatureFlags.AutoBanEvaders"/> enabled,
    /// users that Twitch identifies as <em>likely</em> ban evaders are automatically banned
    /// and the Forge announces their removal in chat.
    /// </summary>
    public class SuspiciousUserMessageHandler : BaseEventSubHandler
    {
        public SuspiciousUserMessageHandler(IServiceScopeFactory scopeFactory, ILogger<SuspiciousUserMessageHandler> logger)
            : base(scopeFactory, logger)
        {
        }

        public override string SubscriptionType => "channel.suspicious_user.message";

        public override async Task HandleAsync(JsonElement eventData)
        {
            var payload = UnwrapEvent(eventData);

            if (!TryGetBroadcasterId(payload, out var broadcasterId) || broadcasterId == null)
            {
                Logger.LogWarning("⚠️ SuspiciousUserMessageHandler: missing broadcaster_user_id in payload.");
                return;
            }

            var suspiciousUserId = GetStringProperty(payload, "user_id");
            var suspiciousUserLogin = GetStringProperty(payload, "user_login");
            var banEvasionEvaluation = GetStringProperty(payload, "ban_evasion_evaluation");

            // Only act on confirmed "likely" ban evaders; ignore "possible" to avoid false positives.
            if (banEvasionEvaluation != "likely")
            {
                Logger.LogInformation(
                    "⏭️ Skipping suspicious user {UserLogin} ({UserId}) in channel {BroadcasterId}: ban_evasion_evaluation={Evaluation} (only 'likely' triggers auto-ban).",
                    suspiciousUserLogin, suspiciousUserId, broadcasterId, banEvasionEvaluation);
                return;
            }

            // Check that the types array contains "ban_evader".
            if (!payload.TryGetProperty("types", out var typesElement) || typesElement.ValueKind != JsonValueKind.Array)
            {
                Logger.LogInformation(
                    "⏭️ Skipping suspicious user {UserLogin} ({UserId}) in channel {BroadcasterId}: 'types' array missing.",
                    suspiciousUserLogin, suspiciousUserId, broadcasterId);
                return;
            }

            var types = typesElement.EnumerateArray()
                .Select(t => t.GetString() ?? string.Empty)
                .ToList();

            if (!types.Contains("ban_evader"))
            {
                Logger.LogInformation(
                    "⏭️ Skipping suspicious user {UserLogin} ({UserId}) in channel {BroadcasterId}: types={Types} (no 'ban_evader' flag).",
                    suspiciousUserLogin, suspiciousUserId, broadcasterId, string.Join(", ", types));
                return;
            }

            if (string.IsNullOrEmpty(suspiciousUserId))
            {
                Logger.LogWarning("⚠️ SuspiciousUserMessageHandler: user_id missing in payload for channel {BroadcasterId}.", broadcasterId);
                return;
            }

            using var scope = ScopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetService<IUserRepository>();
            if (userRepository == null)
            {
                Logger.LogWarning("⚠️ SuspiciousUserMessageHandler: IUserRepository not available.");
                return;
            }

            var user = await userRepository.GetUserAsync(broadcasterId);
            if (user == null)
            {
                Logger.LogWarning("⚠️ SuspiciousUserMessageHandler: broadcaster {BroadcasterId} not found in database.", broadcasterId);
                return;
            }

            if (!user.Features.AutoBanEvaders)
            {
                Logger.LogInformation(
                    "⏭️ AutoBanEvaders is disabled for broadcaster {BroadcasterId}. Skipping auto-ban for {UserLogin}.",
                    broadcasterId, suspiciousUserLogin);
                return;
            }

            var twitchApiService = scope.ServiceProvider.GetService<ITwitchApiService>();
            if (twitchApiService == null)
            {
                Logger.LogWarning("⚠️ SuspiciousUserMessageHandler: ITwitchApiService not available.");
                return;
            }

            Logger.LogInformation(
                "🔨 Auto-banning likely ban evader {UserLogin} ({UserId}) in channel {BroadcasterId}.",
                suspiciousUserLogin, suspiciousUserId, broadcasterId);

            var banReason = "Auto-banned: unwelcome user removed by OmniForge";

            await twitchApiService.BanUserAsync(
                broadcasterId,
                suspiciousUserId,
                banReason);

            // Announce the removal in chat using the Forge bot.
            var botCredentialRepository = scope.ServiceProvider.GetService<IBotCredentialRepository>();
            if (botCredentialRepository != null)
            {
                var botCreds = await botCredentialRepository.GetAsync();
                if (botCreds != null && !string.IsNullOrEmpty(botCreds.UserId))
                {
                    var announcement = $"⚒️ The Forge has spoken. {suspiciousUserLogin} — bad customers are not welcome here.";
                    await twitchApiService.SendChatMessageAsBotAsync(broadcasterId, botCreds.UserId, announcement);
                }
            }
        }
    }
}
