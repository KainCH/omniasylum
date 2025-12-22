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

        private static readonly Dictionary<string, ChatCommandDefinition> _defaultCommands = new()
        {
            { "!deaths", new ChatCommandDefinition { Response = "Current death count: {{deaths}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
            { "!swears", new ChatCommandDefinition { Response = "Current swear count: {{swears}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
            { "!sw", new ChatCommandDefinition { Response = "Current swear count: {{swears}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
            { "!screams", new ChatCommandDefinition { Response = "Current scream count: {{screams}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
            { "!sc", new ChatCommandDefinition { Response = "Current scream count: {{screams}}", Permission = "everyone", Cooldown = 5, Enabled = true } },
            { "!stats", new ChatCommandDefinition { Response = "Deaths: {{deaths}}, Swears: {{swears}}, Screams: {{screams}}, Bits: {{bits}}", Permission = "everyone", Cooldown = 10, Enabled = true } },
            { "!death+", new ChatCommandDefinition { Action = "increment", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!death-", new ChatCommandDefinition { Action = "decrement", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!d+", new ChatCommandDefinition { Action = "increment", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!d-", new ChatCommandDefinition { Action = "decrement", Counter = "deaths", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!swear+", new ChatCommandDefinition { Action = "increment", Counter = "swears", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!swear-", new ChatCommandDefinition { Action = "decrement", Counter = "swears", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!sw+", new ChatCommandDefinition { Action = "increment", Counter = "swears", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!sw-", new ChatCommandDefinition { Action = "decrement", Counter = "swears", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!scream+", new ChatCommandDefinition { Action = "increment", Counter = "screams", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!scream-", new ChatCommandDefinition { Action = "decrement", Counter = "screams", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!sc+", new ChatCommandDefinition { Action = "increment", Counter = "screams", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!sc-", new ChatCommandDefinition { Action = "decrement", Counter = "screams", Permission = "moderator", Cooldown = 1, Enabled = true } },
            { "!resetcounters", new ChatCommandDefinition { Action = "reset", Permission = "broadcaster", Cooldown = 10, Enabled = true } }
        };

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

            var commandText = parts[0].ToLowerInvariant();
            // Space separated amounts (e.g. !sw+ 5) are disabled.
            // Only attached amounts (e.g. !sw+5 or !sw5+) are supported via ResolveCommand.
            int? requestedAmount = null;

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

                    var previousDeaths = counters.Deaths;
                    var previousSwears = counters.Swears;
                    var previousScreams = counters.Screams;

                    bool changed = false;

                    // Resolve command (exact match or attached number like !sw+5)
                    var (cmdConfig, attachedAmount, baseCommandKey) = ResolveCommand(commandText, chatCommands);

                    if (cmdConfig != null)
                    {
                        if (!cmdConfig.Enabled) return;

                        // Determine amount: Attached > Requested > Default(1)
                        var amountToUse = attachedAmount ?? requestedAmount ?? 1;
                        var amount = Math.Clamp(amountToUse, 1, maxIncrement);

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

                            // Use the resolved base command key for cooldown consistency
                            var cooldownKey = baseCommandKey ?? commandText;

                            if (userCooldowns.TryGetValue(cooldownKey, out var lastUsed) && (now - lastUsed).TotalSeconds < cmdConfig.Cooldown)
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
                                    // Replace template variables
                                    var response = cmdConfig.Response;
                                    response = ReplaceVariables(response, counters);
                                    await TrySend(sendMessage, context.UserId, response);
                                }

                                userCooldowns[cooldownKey] = now;
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

        private (ChatCommandDefinition? Config, int? AttachedAmount, string? BaseCommandKey) ResolveCommand(
            string commandText,
            ChatCommandConfiguration userConfig)
        {
            // 1. Try exact match
            if (userConfig.Commands.TryGetValue(commandText, out var userCmd)) return (userCmd, null, commandText);
            if (_defaultCommands.TryGetValue(commandText, out var defCmd)) return (defCmd, null, commandText);

            // 2. Try parsing attached number (e.g. !sw+5)
            // Regex: ^(.+?)(\d+)$
            var match = System.Text.RegularExpressions.Regex.Match(commandText, @"^(.+?)(\d+)$");
            if (match.Success)
            {
                var baseCmd = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out var amount))
                {
                    if (userConfig.Commands.TryGetValue(baseCmd, out userCmd)) return (userCmd, amount, baseCmd);
                    if (_defaultCommands.TryGetValue(baseCmd, out defCmd)) return (defCmd, amount, baseCmd);
                }
            }

            // 3. Try parsing number inside (e.g. !sw5+)
            // Regex: ^(![a-zA-Z]+)(\d+)([+-])$
            var matchInside = System.Text.RegularExpressions.Regex.Match(commandText, @"^(![a-zA-Z]+)(\d+)([+-])$");
            if (matchInside.Success)
            {
                var prefix = matchInside.Groups[1].Value;
                var suffix = matchInside.Groups[3].Value;
                var baseCmd = prefix + suffix;
                if (int.TryParse(matchInside.Groups[2].Value, out var amount))
                {
                    if (userConfig.Commands.TryGetValue(baseCmd, out userCmd)) return (userCmd, amount, baseCmd);
                    if (_defaultCommands.TryGetValue(baseCmd, out defCmd)) return (defCmd, amount, baseCmd);
                }
            }

            return (null, null, null);
        }

        private static bool ApplyActionToCounters(User? user, Counter counters, string action, string? counterTargets, int amount)
        {
            var targets = (counterTargets ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (targets.Length == 0 && action != "reset")
            {
                return false;
            }

            bool anyChanged = false;

            if (action == "reset")
            {
                // If targets specified, reset only those; else reset core counters.
                if (targets.Length == 0)
                {
                    if (counters.Deaths != 0 || counters.Swears != 0 || counters.Screams != 0)
                    {
                        counters.Deaths = 0;
                        counters.Swears = 0;
                        counters.Screams = 0;
                        return true;
                    }
                    return false;
                }

                foreach (var target in targets)
                {
                    if (ApplyDelta(counters, target.ToLowerInvariant(), 0, isReset: true, user: user))
                    {
                        anyChanged = true;
                    }
                }

                return anyChanged;
            }

            var delta = action == "decrement" ? -amount : amount;

            foreach (var target in targets)
            {
                if (ApplyDelta(counters, target.ToLowerInvariant(), delta, isReset: false, user: user))
                {
                    anyChanged = true;
                }
            }

            return anyChanged;
        }

        private static bool ApplyDelta(Counter counters, string target, int delta, bool isReset, User? user)
        {
            int oldValue;
            int newValue;

            switch (target)
            {
                case "deaths":
                    oldValue = counters.Deaths;
                    newValue = isReset ? 0 : Math.Max(0, counters.Deaths + delta);
                    counters.Deaths = newValue;
                    return oldValue != newValue;
                case "swears":
                    oldValue = counters.Swears;
                    newValue = isReset ? 0 : Math.Max(0, counters.Swears + delta);
                    counters.Swears = newValue;
                    return oldValue != newValue;
                case "screams":
                    if (user != null && user.OverlaySettings.Counters.Screams)
                    {
                        oldValue = counters.Screams;
                        newValue = isReset ? 0 : Math.Max(0, counters.Screams + delta);
                        counters.Screams = newValue;
                        return oldValue != newValue;
                    }
                    return false;
                case "bits":
                    oldValue = counters.Bits;
                    newValue = isReset ? 0 : Math.Max(0, counters.Bits + delta);
                    counters.Bits = newValue;
                    return oldValue != newValue;
                default:
                    counters.CustomCounters ??= new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    oldValue = counters.CustomCounters.TryGetValue(target, out var existing) ? existing : 0;
                    newValue = isReset ? 0 : Math.Max(0, oldValue + delta);
                    counters.CustomCounters[target] = newValue;
                    return oldValue != newValue;
            }
        }

        private static string ReplaceVariables(string template, Counter counters)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            var result = template
                .Replace("{{deaths}}", counters.Deaths.ToString())
                .Replace("{{swears}}", counters.Swears.ToString())
                .Replace("{{screams}}", counters.Screams.ToString())
                .Replace("{{bits}}", counters.Bits.ToString());

            // Handle custom counters if needed, though regex is better for dynamic keys
            // For now, simple replacement is enough for default commands

            return result;
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
