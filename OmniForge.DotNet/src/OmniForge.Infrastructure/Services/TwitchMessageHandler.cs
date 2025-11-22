using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
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

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var counterRepository = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
                    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                    var counters = await counterRepository.GetCountersAsync(userId);
                    var user = await userRepository.GetUserAsync(userId);

                    var previousDeaths = counters.Deaths;
                    var previousSwears = counters.Swears;
                    var previousScreams = counters.Screams;

                    // If counters don't exist, create them?
                    // The original code assumed they exist or GetCountersAsync returns a default/new object.
                    // Let's assume GetCountersAsync returns a valid object as per original code.

                    bool changed = false;

                    switch (command)
                    {
                        case "!deaths":
                            await sendMessage(userId, $"Death Count: {counters.Deaths}");
                            break;
                        case "!swears":
                            await sendMessage(userId, $"Swear Count: {counters.Swears}");
                            break;
                        case "!screams":
                            if (user != null && user.OverlaySettings.Counters.Screams)
                            {
                                await sendMessage(userId, $"Scream Count: {counters.Screams}");
                            }
                            break;
                        case "!stats":
                            await sendMessage(userId, $"Deaths: {counters.Deaths} | Swears: {counters.Swears} | Screams: {counters.Screams}");
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
                            break;
                        case "!death-":
                        case "!d-":
                            if (isMod && counters.Deaths > 0)
                            {
                                counters.Deaths--;
                                changed = true;
                                await sendMessage(userId, $"Death Count: {counters.Deaths}");
                            }
                            break;
                        case "!swear+":
                        case "!s+":
                            if (isMod)
                            {
                                counters.Swears++;
                                changed = true;
                                await sendMessage(userId, $"Swear Count: {counters.Swears}");
                            }
                            break;
                        case "!swear-":
                        case "!s-":
                            if (isMod && counters.Swears > 0)
                            {
                                counters.Swears--;
                                changed = true;
                                await sendMessage(userId, $"Swear Count: {counters.Swears}");
                            }
                            break;
                        case "!scream+":
                        case "!sc+":
                            if (isMod && user != null && user.OverlaySettings.Counters.Screams)
                            {
                                counters.Screams++;
                                changed = true;
                                await sendMessage(userId, $"Scream Count: {counters.Screams}");
                            }
                            break;
                        case "!scream-":
                        case "!sc-":
                            if (isMod && counters.Screams > 0 && user != null && user.OverlaySettings.Counters.Screams)
                            {
                                counters.Screams--;
                                changed = true;
                                await sendMessage(userId, $"Scream Count: {counters.Screams}");
                            }
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
                            break;
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
                            _logger.LogError(ex, "Error sending Discord notification for {UserId}", userId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message for {UserId}", userId);
            }
        }
    }
}
