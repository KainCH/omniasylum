using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using TwitchLib.Client.Models;

namespace OmniForge.Infrastructure.Services
{
    public interface ITwitchMessageHandler
    {
        Task HandleMessageAsync(string userId, ChatMessage chatMessage, Func<string, string, Task> sendMessage);
    }

    public class TwitchMessageHandler : ITwitchMessageHandler
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<TwitchMessageHandler> _logger;

        // Cooldown tracking: UserId -> Command -> LastUsedTime
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>> _cooldowns = new();

        public TwitchMessageHandler(
            IServiceScopeFactory scopeFactory,
            IOverlayNotifier overlayNotifier,
            ILogger<TwitchMessageHandler> logger)
        {
            _scopeFactory = scopeFactory;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public async Task HandleMessageAsync(string userId, ChatMessage chatMessage, Func<string, string, Task> sendMessage)
        {
            if (!chatMessage.Message.StartsWith("!")) return;

            var command = chatMessage.Message.ToLower().Split(' ')[0];
            var isMod = chatMessage.IsModerator || chatMessage.IsBroadcaster;
            var isSubscriber = chatMessage.IsSubscriber;

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                    var counters = await counterRepository.GetCountersAsync(userId) ?? new Counter { TwitchUserId = userId };
                    var user = await userRepository.GetUserAsync(userId);

                    var previousDeaths = counters.Deaths;
                    var previousSwears = counters.Swears;
                    var previousScreams = counters.Screams;

                    bool changed = false;
                    bool handled = false;

                    switch (command)
                    {
                        case "!deaths":
                            await sendMessage(userId, $"Death Count: {counters.Deaths}");
                            handled = true;
                            break;
                        case "!swears":
                            await sendMessage(userId, $"Swear Count: {counters.Swears}");
                            handled = true;
                            break;
                        case "!screams":
                            if (user != null && user.OverlaySettings.Counters.Screams)
                            {
                                await sendMessage(userId, $"Scream Count: {counters.Screams}");
                            }
                            handled = true;
                            break;
                        case "!stats":
                            await sendMessage(userId, $"Deaths: {counters.Deaths} | Swears: {counters.Swears} | Screams: {counters.Screams}");
                            handled = true;
                            break;

                        // Mod-only commands
                        case "!death+":
                        case "!d+":
                            if (isMod)
                            {
                                counters.Deaths++;
                                changed = true;
                                await sendMessage(userId, $"Death Count: {counters.Deaths}");
                            }
                            handled = true;
                            break;
                        case "!death-":
                        case "!d-":
                            if (isMod && counters.Deaths > 0)
                            {
                                counters.Deaths--;
                                changed = true;
                                await sendMessage(userId, $"Death Count: {counters.Deaths}");
                            }
                            handled = true;
                            break;
                        case "!swear+":
                        case "!s+":
                            if (isMod)
                            {
                                counters.Swears++;
                                changed = true;
                                await sendMessage(userId, $"Swear Count: {counters.Swears}");
                            }
                            handled = true;
                            break;
                        case "!swear-":
                        case "!s-":
                            if (isMod && counters.Swears > 0)
                            {
                                counters.Swears--;
                                changed = true;
                                await sendMessage(userId, $"Swear Count: {counters.Swears}");
                            }
                            handled = true;
                            break;
                        case "!scream+":
                        case "!sc+":
                            if (isMod && user != null && user.OverlaySettings.Counters.Screams)
                            {
                                counters.Screams++;
                                changed = true;
                                await sendMessage(userId, $"Scream Count: {counters.Screams}");
                            }
                            handled = true;
                            break;
                        case "!scream-":
                        case "!sc-":
                            if (isMod && counters.Screams > 0 && user != null && user.OverlaySettings.Counters.Screams)
                            {
                                counters.Screams--;
                                changed = true;
                                await sendMessage(userId, $"Scream Count: {counters.Screams}");
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
                                await sendMessage(userId, "Counters have been reset.");
                            }
                            handled = true;
                            break;
                    }

                    // Handle Custom Commands
                    if (!handled)
                    {
                        var chatCommands = await userRepository.GetChatCommandsConfigAsync(userId);
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
                                    hasPermission = chatMessage.IsBroadcaster;
                                    break;
                                default:
                                    hasPermission = true;
                                    break;
                            }

                            if (hasPermission)
                            {
                                // Check Cooldown
                                var userCooldowns = _cooldowns.GetOrAdd(userId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>());
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
                                    await sendMessage(userId, cmdConfig.Response);
                                    userCooldowns[command] = now;
                                }
                            }
                        }
                    }

                    if (changed)
                    {
                        counters.LastUpdated = DateTimeOffset.UtcNow;
                        await counterRepository.SaveCountersAsync(counters);
                        await _overlayNotifier.NotifyCounterUpdateAsync(userId, counters);

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
                            _logger.LogError(ex, "Error sending Discord notification for {UserId}", LogSanitizer.Sanitize(userId));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message for {UserId}", LogSanitizer.Sanitize(userId));
            }
        }
    }
}
