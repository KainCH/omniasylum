using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;

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
                    var counterLibraryRepository = scope.ServiceProvider.GetService<ICounterLibraryRepository>();
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
                    var (cmdConfig, attachedAmount) = ResolveCommand(commandText, chatCommands);

                    if (cmdConfig == null)
                    {
                        var handledCustom = await TryHandleCustomCounterCommandAsync(
                            context,
                            commandText,
                            requestedAmount,
                            maxIncrement,
                            isMod,
                            sendMessage,
                            counterRepository,
                            counterLibraryRepository,
                            counters);

                        if (handledCustom)
                        {
                            return;
                        }
                    }

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
                            // Use the base command name for cooldown key, not the full text (so !sw+5 shares cooldown with !sw+)
                            // We don't have the base command name easily here unless we return it from ResolveCommand.
                            // For now, use commandText which might be !sw+5. Ideally should be !sw+.
                            // Let's improve ResolveCommand to return the base key.

                            // Re-resolving key for cooldown consistency
                            var cooldownKey = commandText;
                            if (attachedAmount.HasValue)
                            {
                                // If attached amount, strip digits to get base key
                                var match = System.Text.RegularExpressions.Regex.Match(commandText, @"^(.+?)(\d+)$");
                                if (match.Success) cooldownKey = match.Groups[1].Value;
                            }

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

        private async Task<bool> TryHandleCustomCounterCommandAsync(
            ChatCommandContext context,
            string commandText,
            int? requestedAmount,
            int maxIncrement,
            bool isMod,
            Func<string, string, Task>? sendMessage,
            ICounterRepository counterRepository,
            ICounterLibraryRepository? counterLibraryRepository,
            Counter currentCounters)
        {
            // Supported:
            // - !<counterId>      (show current value)
            // - !<counterId>+     (increment, mod/broadcaster)
            // - !<counterId>-     (decrement, mod/broadcaster)
            // - !<counterId>+5    (attached amount)
            // - !<counterId>+ 5   (space amount - parsed upstream into requestedAmount)

            // NOTE: Counter IDs may include '-' but a trailing '-' is interpreted as the decrement operator.
            // This pattern allows internal hyphens but prevents the trailing operator from being consumed by the id.
            var match = Regex.Match(commandText, @"^!(?<id>[a-z0-9_]+(?:-[a-z0-9_]+)*)(?<op>[\+\-])?(?<num>\d+)?$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            var requestedId = match.Groups["id"].Value;
            var op = match.Groups["op"].Success ? match.Groups["op"].Value : null;

            int? attachedAmount = null;
            if (match.Groups["num"].Success && int.TryParse(match.Groups["num"].Value, out var parsed) && parsed > 0)
            {
                attachedAmount = parsed;
            }

            CustomCounterConfiguration? customConfig;
            try
            {
                customConfig = await counterRepository.GetCustomCountersConfigAsync(context.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed loading active custom counters config for chat command user {UserId}", LogSanitizer.Sanitize(context.UserId));
                return false;
            }

            if (customConfig?.Counters == null || customConfig.Counters.Count == 0)
            {
                return false;
            }

            var actualCounterId = await ResolveCustomCounterIdAsync(customConfig, requestedId, counterLibraryRepository);
            if (string.IsNullOrWhiteSpace(actualCounterId))
            {
                return false;
            }

            actualCounterId = actualCounterId.Trim();

            // Explicit validation for storage-safe counter IDs.
            const int maxCounterIdLength = 64;
            var isValidCounterId = actualCounterId.Length <= maxCounterIdLength
                && actualCounterId.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');

            if (!isValidCounterId)
            {
                _logger.LogWarning(
                    "⚠️ Rejected custom counter command with invalid counterId '{CounterId}' for user {UserId}",
                    LogSanitizer.Sanitize(actualCounterId),
                    LogSanitizer.Sanitize(context.UserId));
                return false;
            }

            if (!customConfig.Counters.TryGetValue(actualCounterId, out var def))
            {
                return false;
            }

            // Cooldown: keep it simple and consistent with defaults.
            var cooldownSeconds = op == null ? 5 : 1;
            var userCooldowns = _cooldowns.GetOrAdd(context.UserId, _ => new ConcurrentDictionary<string, DateTimeOffset>());
            var now = DateTimeOffset.UtcNow;
            // Use the trigger command text as the cooldown key so id/alias/long are all independently usable.
            var cooldownKey = commandText;
            if (attachedAmount.HasValue)
            {
                var baseMatch = Regex.Match(commandText, @"^(.+?)(\d+)$", RegexOptions.IgnoreCase);
                if (baseMatch.Success)
                {
                    cooldownKey = baseMatch.Groups[1].Value;
                }
            }

            if (userCooldowns.TryGetValue(cooldownKey, out var lastUsed) && (now - lastUsed).TotalSeconds < cooldownSeconds)
            {
                return true;
            }

            // Read current value (best-effort, case-insensitive).
            var previousValue = 0;
            if (currentCounters.CustomCounters != null && currentCounters.CustomCounters.Count > 0)
            {
                var currentKey = currentCounters.CustomCounters.Keys.FirstOrDefault(k => string.Equals(k, actualCounterId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(currentKey) && currentCounters.CustomCounters.TryGetValue(currentKey, out var existing))
                {
                    previousValue = existing;
                }
            }

            // Query command: !<counterId>
            if (op == null)
            {
                var counterName = string.IsNullOrWhiteSpace(def.Name) ? actualCounterId : def.Name;
                await TrySend(sendMessage, context.UserId, $"Current {counterName}: {previousValue}");
                userCooldowns[cooldownKey] = now;
                return true;
            }

            // Mutation commands are mod/broadcaster only.
            if (!isMod)
            {
                return true;
            }

            var amountToUse = attachedAmount ?? requestedAmount ?? 1;
            var amount = Math.Clamp(amountToUse, 1, maxIncrement);

            var incrementBy = Math.Max(1, def.IncrementBy);
            var decrementBy = Math.Max(1, def.DecrementBy);

            var updatedCounters = op == "+"
                ? await counterRepository.IncrementCounterAsync(context.UserId, actualCounterId, amount * incrementBy)
                : await counterRepository.DecrementCounterAsync(context.UserId, actualCounterId, amount * decrementBy);

            await _overlayNotifier.NotifyCounterUpdateAsync(context.UserId, updatedCounters);

            if (op == "+" && def.Milestones != null && def.Milestones.Any())
            {
                var newValue = GetCustomCounterValueCaseInsensitive(updatedCounters.CustomCounters, actualCounterId);

                var crossed = def.Milestones.Where(m => previousValue < m && newValue >= m).ToList();
                foreach (var milestone in crossed)
                {
                    await _overlayNotifier.NotifyCustomAlertAsync(context.UserId, "customMilestoneReached", new
                    {
                        counterId = actualCounterId,
                        counterName = def.Name,
                        milestone,
                        newValue,
                        icon = def.Icon
                    });
                }
            }

            userCooldowns[cooldownKey] = now;
            return true;
        }

        private static int GetCustomCounterValueCaseInsensitive(Dictionary<string, int>? counters, string counterId)
        {
            if (counters == null || counters.Count == 0)
            {
                return 0;
            }

            return counters
                .Where(kvp => string.Equals(kvp.Key, counterId, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Value)
                .FirstOrDefault();
        }

        private static async Task<string?> ResolveCustomCounterIdAsync(
            CustomCounterConfiguration customConfig,
            string requestedToken,
            ICounterLibraryRepository? counterLibraryRepository)
        {
            if (customConfig.Counters == null || customConfig.Counters.Count == 0)
            {
                return null;
            }

            // Fast path: match token to counterId.
            var direct = customConfig.Counters.Keys.FirstOrDefault(k => string.Equals(k, requestedToken, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            // Build trigger map using counter library command metadata.
            if (counterLibraryRepository == null)
            {
                return null;
            }

            var triggerToCounterId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Avoid N+1 queries by fetching the library once.
            var libraryItems = await counterLibraryRepository.ListAsync();
            var enabledIds = new HashSet<string>(customConfig.Counters.Keys.Where(k => !string.IsNullOrWhiteSpace(k)), StringComparer.OrdinalIgnoreCase);
            var libraryById = libraryItems
                .Where(i => !string.IsNullOrWhiteSpace(i.CounterId) && enabledIds.Contains(i.CounterId))
                .ToDictionary(i => i.CounterId, i => i, StringComparer.OrdinalIgnoreCase);

            foreach (var counterId in customConfig.Counters.Keys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                triggerToCounterId[counterId] = counterId;

                if (!libraryById.TryGetValue(counterId, out var item))
                {
                    // Still allow the implicit command based on id (already added above).
                    continue;
                }

                var defaultBase = $"!{counterId}";
                var primary = NormalizeBaseCommandOrDefault(item.LongCommand, defaultBase);
                var alias = NormalizeBaseCommandOrEmpty(item.AliasCommand);

                var primaryToken = primary.TrimStart('!');
                if (!string.IsNullOrWhiteSpace(primaryToken))
                {
                    triggerToCounterId[primaryToken] = counterId;
                }

                var aliasToken = alias.TrimStart('!');
                if (!string.IsNullOrWhiteSpace(aliasToken))
                {
                    triggerToCounterId[aliasToken] = counterId;
                }
            }

            return triggerToCounterId.TryGetValue(requestedToken, out var resolved)
                ? resolved
                : null;
        }

        private static string NormalizeBaseCommandOrDefault(string? command, string fallback)
        {
            var normalized = NormalizeBaseCommandOrEmpty(command);
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "!", StringComparison.Ordinal))
            {
                normalized = NormalizeBaseCommandOrEmpty(fallback);
            }

            return normalized;
        }

        private static string NormalizeBaseCommandOrEmpty(string? command)
        {
            var c = (command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(c)) return string.Empty;

            if (!c.StartsWith("!", StringComparison.Ordinal))
            {
                c = "!" + c;
            }

            c = c.TrimEnd('+', '-');
            return c.ToLowerInvariant();
        }

        private (ChatCommandDefinition? Config, int? AttachedAmount) ResolveCommand(
            string commandText,
            ChatCommandConfiguration userConfig)
        {
            // 1. Try exact match
            if (userConfig.Commands.TryGetValue(commandText, out var userCmd)) return (userCmd, null);
            if (_defaultCommands.TryGetValue(commandText, out var defCmd)) return (defCmd, null);

            // 2. Try parsing attached number (e.g. !sw+5)
            // Regex: ^(.+?)(\d+)$
            var match = System.Text.RegularExpressions.Regex.Match(commandText, @"^(.+?)(\d+)$");
            if (match.Success)
            {
                var baseCmd = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out var amount))
                {
                    if (userConfig.Commands.TryGetValue(baseCmd, out userCmd)) return (userCmd, amount);
                    if (_defaultCommands.TryGetValue(baseCmd, out defCmd)) return (defCmd, amount);
                }
            }

            return (null, null);
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

            return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{(\w+)\}\}", match =>
            {
                var key = match.Groups[1].Value.ToLowerInvariant();

                if (key == "deaths") return counters.Deaths.ToString();
                if (key == "swears") return counters.Swears.ToString();
                if (key == "screams") return counters.Screams.ToString();
                if (key == "bits") return counters.Bits.ToString();

                if (counters.CustomCounters != null && counters.CustomCounters.TryGetValue(key, out var customValue))
                {
                    return customValue.ToString();
                }

                return match.Value;
            });
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
