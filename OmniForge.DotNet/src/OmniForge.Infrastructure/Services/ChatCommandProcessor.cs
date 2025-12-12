using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using System.Collections.Concurrent;

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

            var command = context.Message.ToLower().Split(' ')[0];
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

                    var previousDeaths = counters.Deaths;
                    var previousSwears = counters.Swears;
                    var previousScreams = counters.Screams;

                    bool changed = false;
                    bool handled = false;

                    switch (command)
                    {
                        case "!deaths":
                            await TrySend(sendMessage, context.UserId, $"Death Count: {counters.Deaths}");
                            handled = true;
                            break;
                        case "!swears":
                            await TrySend(sendMessage, context.UserId, $"Swear Count: {counters.Swears}");
                            handled = true;
                            break;
                        case "!screams":
                            if (user != null && user.OverlaySettings.Counters.Screams)
                            {
                                await TrySend(sendMessage, context.UserId, $"Scream Count: {counters.Screams}");
                            }
                            handled = true;
                            break;
                        case "!stats":
                            await TrySend(sendMessage, context.UserId, $"Deaths: {counters.Deaths} | Swears: {counters.Swears} | Screams: {counters.Screams}");
                            handled = true;
                            break;

                        // Mod-only commands
                        case "!death+":
                        case "!d+":
                            if (isMod)
                            {
                                counters.Deaths++;
                                changed = true;
                                await TrySend(sendMessage, context.UserId, $"Death Count: {counters.Deaths}");
                            }
                            handled = true;
                            break;
                        case "!death-":
                        case "!d-":
                            if (isMod && counters.Deaths > 0)
                            {
                                counters.Deaths--;
                                changed = true;
                                await TrySend(sendMessage, context.UserId, $"Death Count: {counters.Deaths}");
                            }
                            handled = true;
                            break;
                        case "!swear+":
                        case "!sw+":
                            if (isMod)
                            {
                                counters.Swears++;
                                changed = true;
                                await TrySend(sendMessage, context.UserId, $"Swear Count: {counters.Swears}");
                            }
                            handled = true;
                            break;
                        case "!swear-":
                        case "!sw-":
                            if (isMod && counters.Swears > 0)
                            {
                                counters.Swears--;
                                changed = true;
                                await TrySend(sendMessage, context.UserId, $"Swear Count: {counters.Swears}");
                            }
                            handled = true;
                            break;
                        case "!scream+":
                        case "!sc+":
                            if (isMod && user != null && user.OverlaySettings.Counters.Screams)
                            {
                                counters.Screams++;
                                changed = true;
                                await TrySend(sendMessage, context.UserId, $"Scream Count: {counters.Screams}");
                            }
                            handled = true;
                            break;
                        case "!scream-":
                        case "!sc-":
                            if (isMod && counters.Screams > 0 && user != null && user.OverlaySettings.Counters.Screams)
                            {
                                counters.Screams--;
                                changed = true;
                                await TrySend(sendMessage, context.UserId, $"Scream Count: {counters.Screams}");
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
                                await TrySend(sendMessage, context.UserId, "Counters have been reset.");
                            }
                            handled = true;
                            break;
                    }

                    // Handle Custom Commands
                    if (!handled)
                    {
                        var chatCommands = await userRepository.GetChatCommandsConfigAsync(context.UserId);
                        if (chatCommands.Commands.TryGetValue(command, out var cmdConfig))
                        {
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
                                if (userCooldowns.TryGetValue(command, out var lastUsed))
                                {
                                    if ((now - lastUsed).TotalSeconds < cmdConfig.Cooldown)
                                    {
                                        onCooldown = true;
                                    }
                                }

                                if (!onCooldown)
                                {
                                    await TrySend(sendMessage, context.UserId, cmdConfig.Response);
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
                            var discordService = scope.ServiceProvider.GetRequiredService<IDiscordService>();

                            if (user != null && user.Features.DiscordWebhook && !string.IsNullOrEmpty(user.DiscordWebhookUrl))
                            {
                                // Check Death Milestones
                                if (counters.Deaths > previousDeaths)
                                {
                                    var thresholds = user.DiscordSettings.MilestoneThresholds.Deaths;
                                    if (thresholds.Contains(counters.Deaths))
                                    {
                                        await discordService.SendNotificationAsync(user, "death_milestone", new
                                        {
                                            count = counters.Deaths,
                                            previousMilestone = previousDeaths
                                        });
                                    }
                                }

                                // Check Swear Milestones
                                if (counters.Swears > previousSwears)
                                {
                                    var thresholds = user.DiscordSettings.MilestoneThresholds.Swears;
                                    if (thresholds.Contains(counters.Swears))
                                    {
                                        await discordService.SendNotificationAsync(user, "swear_milestone", new
                                        {
                                            count = counters.Swears,
                                            previousMilestone = previousSwears
                                        });
                                    }
                                }

                                // Check Scream Milestones
                                if (counters.Screams > previousScreams)
                                {
                                    var thresholds = user.DiscordSettings.MilestoneThresholds.Screams;
                                    if (thresholds.Contains(counters.Screams))
                                    {
                                        await discordService.SendNotificationAsync(user, "scream_milestone", new
                                        {
                                            count = counters.Screams,
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
