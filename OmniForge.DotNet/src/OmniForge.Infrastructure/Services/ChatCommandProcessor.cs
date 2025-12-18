using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using System.Collections.Concurrent;
using System.Linq;

namespace OmniForge.Infrastructure.Services
{
    public class ChatCommandContext
    {
        public required string UserId { get; init; }
        public required string Message { get; init; }
        public bool IsModerator { get; init; }
        public bool IsBroadcaster { get; init; }
        public bool IsSubscriber { get; init; }
    }

    public interface IChatCommandProcessor
    {
        Task ProcessAsync(ChatCommandContext context, Func<string, string, Task>? sendMessage = null);
    }

    public class ChatCommandProcessor : IChatCommandProcessor
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<ChatCommandProcessor> _logger;

        // Cooldown tracking: UserId -> Command -> LastUsedTime
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTimeOffset>> _cooldowns = new();

        public ChatCommandProcessor(
            IServiceScopeFactory scopeFactory,
            IOverlayNotifier overlayNotifier,
            ILogger<ChatCommandProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public async Task ProcessAsync(ChatCommandContext context, Func<string, string, Task>? sendMessage = null)
        {
            if (!context.Message.StartsWith("!")) return;

            var parts = context.Message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var command = parts[0].ToLowerInvariant();
            int? requestedAmount = null;
            if (parts.Length > 1 && int.TryParse(parts[1], out var parsedAmount) && parsedAmount > 0)
            {
                requestedAmount = parsedAmount;
            }

            var isMod = context.IsModerator || context.IsBroadcaster;
            var isSubscriber = context.IsSubscriber;

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                    var counters = await counterRepository.GetCountersAsync(context.UserId) ?? new Counter { TwitchUserId = context.UserId };
                    var user = await userRepository.GetUserAsync(context.UserId);

                    var chatCommands = await userRepository.GetChatCommandsConfigAsync(context.UserId) ?? new ChatCommandConfiguration();
                    var maxIncrement = Math.Clamp(chatCommands.MaxIncrementAmount, 1, 10);
                    var amount = Math.Clamp(requestedAmount ?? 1, 1, maxIncrement);

                    var previousDeaths = counters.Deaths;
                    var previousSwears = counters.Swears;
                    var previousScreams = counters.Screams;

                    bool changed = false;
                    bool handled = false;

                    switch (command)
                    {
                        case "!deaths":
                            handled = true;
                            break;
                        case "!swears":
                            handled = true;
                            break;
                        case "!screams":
                            if (user != null && user.OverlaySettings.Counters.Screams)
                            {
                                // Counter commands are intentionally silent (no chat replies)
                            }
                            handled = true;
                            break;
                        case "!stats":
                            handled = true;
                            break;

                        // Mod-only commands
                        case "!death+":
                        case "!d+":
                            if (isMod)
                            {
                                counters.Deaths += amount;
                                changed = true;
                            }
                            handled = true;
                            break;
                        case "!death-":
                        case "!d-":
                            if (isMod && counters.Deaths > 0)
                            {
                                counters.Deaths = Math.Max(0, counters.Deaths - amount);
                                changed = true;
                            }
                            handled = true;
                            break;
                        case "!swear+":
                        case "!sw+":
                            if (isMod)
                            {
                                counters.Swears += amount;
                                changed = true;
                            }
                            handled = true;
                            break;
                        case "!swear-":
                        case "!sw-":
                            if (isMod && counters.Swears > 0)
                            {
                                counters.Swears = Math.Max(0, counters.Swears - amount);
                                changed = true;
                            }
                            handled = true;
                            break;
                        case "!scream+":
                        case "!sc+":
                            if (isMod && user != null && user.OverlaySettings.Counters.Screams)
                            {
                                counters.Screams += amount;
                                changed = true;
                            }
                            handled = true;
                            break;
                        case "!scream-":
                        case "!sc-":
                            if (isMod && counters.Screams > 0 && user != null && user.OverlaySettings.Counters.Screams)
                            {
                                counters.Screams = Math.Max(0, counters.Screams - amount);
                                changed = true;
                            }
                            handled = true;
                            break;
                        case "!resetcounters":
                            if (isMod)
                            {
                                counters.Deaths = 0;
                                counters.Swears = 0;
                                counters.Screams = 0;
                                changed = true;
                            }
                            handled = true;
                            break;
                    }

                    // Handle Custom Commands
                    if (!handled)
                    {
                        if (chatCommands.Commands.TryGetValue(command, out var cmdConfig))
                        {
                            if (!cmdConfig.Enabled) return;

                            // Check Permission
                            bool hasPermission = false;
                            switch (cmdConfig.Permission.ToLower())
                            {
                                case "everyone":
                                    hasPermission = true;
                                    break;
                                case "subscriber":
                                    hasPermission = isSubscriber || isMod; // Mods/Broadcaster imply sub access usually
                                    break;
                                case "moderator":
                                    hasPermission = isMod;
                                    break;
                                case "broadcaster":
                                    hasPermission = context.IsBroadcaster;
                                    break;
                                default:
                                    hasPermission = true;
                                    break;
                            }

                            if (hasPermission)
                            {
                                // Check Cooldown
                                var userCooldowns = _cooldowns.GetOrAdd(context.UserId, _ => new ConcurrentDictionary<string, DateTimeOffset>());
                                var now = DateTimeOffset.UtcNow;

                                bool onCooldown = false;
                                if (userCooldowns.TryGetValue(command, out var lastUsed) && (now - lastUsed).TotalSeconds < cmdConfig.Cooldown)
                                {
                                    onCooldown = true;
                                }

                                if (!onCooldown)
                                {
                                    var action = cmdConfig.Action?.ToLowerInvariant();
                                    if (!string.IsNullOrWhiteSpace(action))
                                    {
                                        changed = ApplyActionToCounters(user, counters, action, cmdConfig.Counter, amount) || changed;
                                    }
                                    else
                                    {
                                        await TrySend(sendMessage, context.UserId, cmdConfig.Response);
                                    }

                                    userCooldowns[command] = now;
                                }
                            }
                        }
                    }

                    if (changed)
                    {
                        counters.LastUpdated = DateTimeOffset.UtcNow;
                        await counterRepository.SaveCountersAsync(counters);
                        await _overlayNotifier.NotifyCounterUpdateAsync(context.UserId, counters);

                        // Check for milestones
                        try
                        {
                            if (user != null && user.Features.DiscordNotifications)
                            {
                                var discordSettings = user.DiscordSettings ?? new DiscordSettings();
                                var hasChannelOverride = discordSettings.MessageTemplates?.Values.Any(t => !string.IsNullOrWhiteSpace(t.ChannelIdOverride)) == true;
                                var hasDestination = !string.IsNullOrWhiteSpace(user.DiscordChannelId) || !string.IsNullOrWhiteSpace(user.DiscordWebhookUrl) || hasChannelOverride;
                                if (!hasDestination)
                                {
                                    // DiscordService also no-ops without a destination, but avoid invoking it at all.
                                    return;
                                }

                                var discordService = scope.ServiceProvider.GetRequiredService<IDiscordService>();

                                // Check Death Milestones
                                if (discordSettings.EnabledNotifications.DeathMilestone && counters.Deaths > previousDeaths)
                                {
                                    var thresholds = discordSettings.MilestoneThresholds.Deaths;
                                    foreach (var threshold in thresholds.Where(t => t > previousDeaths && t <= counters.Deaths))
                                    {
                                        await discordService.SendNotificationAsync(user, "death_milestone", new
                                        {
                                            count = threshold,
                                            previousMilestone = previousDeaths
                                        });
                                    }
                                }

                                // Check Swear Milestones
                                if (discordSettings.EnabledNotifications.SwearMilestone && counters.Swears > previousSwears)
                                {
                                    var thresholds = discordSettings.MilestoneThresholds.Swears;
                                    foreach (var threshold in thresholds.Where(t => t > previousSwears && t <= counters.Swears))
                                    {
                                        await discordService.SendNotificationAsync(user, "swear_milestone", new
                                        {
                                            count = threshold,
                                            previousMilestone = previousSwears
                                        });
                                    }
                                }

                                // Check Scream Milestones
                                if (discordSettings.EnabledNotifications.ScreamMilestone && counters.Screams > previousScreams)
                                {
                                    var thresholds = discordSettings.MilestoneThresholds.Screams;
                                    foreach (var threshold in thresholds.Where(t => t > previousScreams && t <= counters.Screams))
                                    {
                                        await discordService.SendNotificationAsync(user, "scream_milestone", new
                                        {
                                            count = threshold,
                                            previousMilestone = previousScreams
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error sending Discord notification for {UserId}", LogSanitizer.Sanitize(context.UserId));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chat command for {UserId}", LogSanitizer.Sanitize(context.UserId));
            }
        }

        private static bool ApplyActionToCounters(User? user, Counter counters, string action, string? counterTargets, int amount)
        {
            var targets = (counterTargets ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (targets.Length == 0 && action != "reset")
            {
                return false;
            }

            if (action == "reset")
            {
                // If targets specified, reset only those; else reset core counters.
                if (targets.Length == 0)
                {
                    counters.Deaths = 0;
                    counters.Swears = 0;
                    counters.Screams = 0;
                    return true;
                }

                foreach (var target in targets)
                {
                    ApplyDelta(counters, target.ToLowerInvariant(), 0, isReset: true, user: user);
                }

                return true;
            }

            var delta = action == "decrement" ? -amount : amount;

            foreach (var target in targets)
            {
                ApplyDelta(counters, target.ToLowerInvariant(), delta, isReset: false, user: user);
            }

            return true;
        }

        private static void ApplyDelta(Counter counters, string target, int delta, bool isReset, User? user)
        {
            switch (target)
            {
                case "deaths":
                    counters.Deaths = isReset ? 0 : Math.Max(0, counters.Deaths + delta);
                    break;
                case "swears":
                    counters.Swears = isReset ? 0 : Math.Max(0, counters.Swears + delta);
                    break;
                case "screams":
                    if (user != null && user.OverlaySettings.Counters.Screams)
                    {
                        counters.Screams = isReset ? 0 : Math.Max(0, counters.Screams + delta);
                    }
                    break;
                case "bits":
                    counters.Bits = isReset ? 0 : Math.Max(0, counters.Bits + delta);
                    break;
                default:
                    counters.CustomCounters ??= new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var current = counters.CustomCounters.TryGetValue(target, out var existing) ? existing : 0;
                    counters.CustomCounters[target] = isReset ? 0 : Math.Max(0, current + delta);
                    break;
            }
        }

        private static async Task TrySend(Func<string, string, Task>? sendMessage, string userId, string message)
        {
            if (sendMessage == null) return;
            try
            {
                await sendMessage(userId, message);
            }
            catch
            {
                // Swallow send failures to not break processing; logging happens at caller level if needed.
            }
        }
    }
}
