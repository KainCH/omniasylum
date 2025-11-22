using System;
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
                    var counters = await counterRepository.GetCountersAsync(userId);

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
