using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Utilities;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
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

        // Cache for custom counter trigger resolution: (userId + enabledIdsKey) -> trigger map
        private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, Dictionary<string, string> TriggerToCounterId)> _customCounterTriggerCache = new();

        // Shared cache for counter library items to avoid repeated ListAsync calls.
        // Cache is per repository instance to avoid cross-instance pollution (and to keep tests isolated).
        private sealed class CounterLibraryCacheEntry
        {
            public DateTimeOffset ExpiresAt { get; set; }
            public CounterLibraryItem[] Items { get; set; } = Array.Empty<CounterLibraryItem>();
            public SemaphoreSlim Gate { get; } = new(1, 1);
        }

        private static readonly ConditionalWeakTable<ICounterLibraryRepository, CounterLibraryCacheEntry> CounterLibraryCache = new();

        // Inline amount between command base and operation, e.g. !d5+ or !death10-
        // We only treat the digits as an amount if the derived base command exists.
        private static readonly Regex InlineAmountBeforeOpRegex = new(
            @"^(![^\d\s\+\-:]+?)(\d{1,2})([\+\-])$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex CustomCounterCommandRegex = new(
            @"^!(?<id>[a-z0-9_]+(?:-[a-z0-9_]+)*)(?:(?<op>[\+\-]))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Inline amount right before + / - for custom counters too, e.g. !pulls5+ or !boss10-
        private static readonly Regex InlineAmountCustomCounterRegex = new(
            @"^!(?<id>[a-z0-9_]+(?:-[a-z0-9_]+)*?)(?<num>\d{1,2})(?<op>[\+\-])$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Custom counter trigger cache expiration.
        // 5 minutes: reduces DB reads on chat spam while keeping admin edits reasonably fresh.
        private static readonly TimeSpan CustomCounterTriggerCacheTtl = TimeSpan.FromMinutes(5);

        // Counter library cache expiration.
        // Kept the same as the trigger cache for simplicity.
        private static readonly TimeSpan CounterLibraryCacheTtl = TimeSpan.FromMinutes(5);
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

            // Twitch chat (especially mobile) can contain non-breaking spaces or other unicode whitespace.
            // Normalize and split on any whitespace for consistent parsing (e.g. action commands like "!both 2").
            var normalizedMessage = context.Message.Replace('\u00A0', ' ').Trim();
            var parts = normalizedMessage.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var commandText = parts[0].ToLowerInvariant();
            int? trailingAmount = null;
            if (parts.Length > 1 && int.TryParse(parts[1], out var parsedTrailing) && parsedTrailing > 0)
            {
                trailingAmount = parsedTrailing;
            }
            var hasTrailingNumericArgument = trailingAmount.HasValue;

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

                    // Resolve command (exact match or inline amount like !d5+)
                    var (cmdConfig, inlineAmount) = ResolveCommand(commandText, chatCommands);

                    if (cmdConfig == null)
                    {
                        var handledCustom = await TryHandleCustomCounterCommandAsync(
                            context,
                            commandText,
                            hasTrailingNumericArgument,
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

                        // If this is an increment/decrement command targeting a custom counter, route through
                        // the custom counter handler so we respect IncrementBy/DecrementBy, milestones, and
                        // storage-safe atomic updates.
                        var actionForRouting = cmdConfig.Action?.ToLowerInvariant();
                        if ((actionForRouting == "increment" || actionForRouting == "decrement")
                            && !string.IsNullOrWhiteSpace(cmdConfig.Counter)
                            && cmdConfig.Counter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Any(t => t is not "deaths" and not "swears" and not "screams" and not "bits"))
                        {
                            var handledCustom = await TryHandleCustomCounterCommandAsync(
                                context,
                                commandText,
                                hasTrailingNumericArgument,
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

                        // Old formats like "!cmd+ 5" are no longer supported. Avoid silently applying +1.
                        var isDirectMutationCommand = commandText.EndsWith('+') || commandText.EndsWith('-');
                        if (hasTrailingNumericArgument && isDirectMutationCommand && (actionForRouting == "increment" || actionForRouting == "decrement"))
                        {
                            return;
                        }

                        // Determine amount:
                        // - Inline amounts apply to direct mutation commands only (e.g. !d5+).
                        // - Trailing numeric args apply to action commands without +/- suffix (e.g. !both 2).
                        // - Otherwise default to 1.
                        var amountToUse = inlineAmount
                            ?? (!isDirectMutationCommand
                                && trailingAmount.HasValue
                                && (actionForRouting == "increment" || actionForRouting == "decrement")
                                    ? trailingAmount.Value
                                    : 1);
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

                            // Cooldown key: prefer base command so !d5+ shares cooldown with !d+
                            var cooldownKey = commandText;
                            var cooldownInline = InlineAmountBeforeOpRegex.Match(commandText);
                            if (cooldownInline.Success)
                            {
                                var baseCmd = $"{cooldownInline.Groups[1].Value.ToLowerInvariant()}{cooldownInline.Groups[3].Value}";
                                cooldownKey = baseCmd;
                            }

                            var onCooldown = userCooldowns.TryGetValue(cooldownKey, out var lastUsed)
                                && (now - lastUsed).TotalSeconds < cmdConfig.Cooldown;

                            if (!onCooldown)
                            {
                                var action = actionForRouting;
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
            bool hasTrailingNumericArgument,
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
            // - !<counterId>5+    (inline amount 2-10 before operator)

            // Old formats like "!counter+ 5" are no longer supported. Avoid silently applying +1.
            if (hasTrailingNumericArgument)
            {
                return true;
            }

            // Inline amount (e.g. !pulls5+): strip the digits out of the command token.
            var inline = InlineAmountCustomCounterRegex.Match(commandText);
            int? inlineAmount = null;
            if (inline.Success
                && int.TryParse(inline.Groups["num"].Value, out var parsedInlineAmount)
                && parsedInlineAmount >= 2
                && parsedInlineAmount <= 10)
            {
                var baseId = inline.Groups["id"].Value;
                var inlineOp = inline.Groups["op"].Value;
                commandText = $"!{baseId}{inlineOp}";
                inlineAmount = parsedInlineAmount;
            }

            // NOTE: Counter IDs may include '-' but a trailing '-' is interpreted as the decrement operator.
            // This pattern allows internal hyphens but prevents the trailing operator from being consumed by the id.
            var match = CustomCounterCommandRegex.Match(commandText);
            if (!match.Success)
            {
                return false;
            }

            var requestedId = match.Groups["id"].Value;
            var op = match.Groups["op"].Success ? match.Groups["op"].Value : null;

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

            var actualCounterId = await ResolveCustomCounterIdAsync(context.UserId, customConfig, requestedId, counterLibraryRepository);
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
            // Use resolved counterId (+ operation) so aliases/variants share cooldown.
            var cooldownKey = op == null ? actualCounterId : $"{actualCounterId}{op}";

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
                _logger.LogDebug(
                    "Ignoring custom counter mutation from non-mod/broadcaster. user_id={UserId} counter_id={CounterId} op={Op}",
                    LogSanitizer.Sanitize(context.UserId),
                    LogSanitizer.Sanitize(actualCounterId),
                    LogSanitizer.Sanitize(op));
                return true;
            }

            var amountToUse = inlineAmount ?? 1;
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

        private async Task<string?> ResolveCustomCounterIdAsync(
            string userId,
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

            var enabledCounterIds = customConfig.Counters.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var enabledIdsKey = string.Join('|', enabledCounterIds);
            var enabledIdsKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(enabledIdsKey)));
            var cacheKey = $"{userId}:{enabledIdsKeyHash}";

            var now = DateTimeOffset.UtcNow;
            if (_customCounterTriggerCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > now)
            {
                return cached.TriggerToCounterId.TryGetValue(requestedToken, out var cachedResolved)
                    ? cachedResolved
                    : null;
            }

            var triggerToCounterId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Avoid N+1 queries by fetching the library once.
            var libraryItems = await GetCounterLibraryItemsCachedAsync(counterLibraryRepository);
            var enabledIds = new HashSet<string>(enabledCounterIds, StringComparer.OrdinalIgnoreCase);
            var libraryById = libraryItems
                .Where(i => !string.IsNullOrWhiteSpace(i.CounterId) && enabledIds.Contains(i.CounterId))
                .ToDictionary(i => i.CounterId, i => i, StringComparer.OrdinalIgnoreCase);

            foreach (var counterId in enabledCounterIds)
            {
                TryAddTrigger(triggerToCounterId, counterId, counterId, userId);

                if (!libraryById.TryGetValue(counterId, out var item))
                {
                    // Still allow the implicit command based on id (already added above).
                    continue;
                }

                var defaultBase = $"!{counterId}";
                var primary = CommandNormalization.NormalizeBaseCommandOrDefault(item.LongCommand, defaultBase);
                var alias = CommandNormalization.NormalizeBaseCommandOrEmpty(item.AliasCommand);

                var primaryToken = primary.TrimStart('!');
                if (!string.IsNullOrWhiteSpace(primaryToken))
                {
                    TryAddTrigger(triggerToCounterId, primaryToken, counterId, userId);
                }

                var aliasToken = alias.TrimStart('!');
                if (!string.IsNullOrWhiteSpace(aliasToken))
                {
                    TryAddTrigger(triggerToCounterId, aliasToken, counterId, userId);
                }
            }

            _customCounterTriggerCache[cacheKey] = (now.Add(CustomCounterTriggerCacheTtl), triggerToCounterId);

            return triggerToCounterId.TryGetValue(requestedToken, out var resolved)
                ? resolved
                : null;
        }

        private async Task<CounterLibraryItem[]> GetCounterLibraryItemsCachedAsync(ICounterLibraryRepository counterLibraryRepository)
        {
            var now = DateTimeOffset.UtcNow;

            var entry = CounterLibraryCache.GetValue(counterLibraryRepository, _ => new CounterLibraryCacheEntry());
            if (entry.ExpiresAt > now)
            {
                return entry.Items;
            }

            await entry.Gate.WaitAsync();
            try
            {
                // Double-check after acquiring the lock.
                if (entry.ExpiresAt > now)
                {
                    return entry.Items;
                }

                CounterLibraryItem[] items;
                try
                {
                    var fetched = await counterLibraryRepository.ListAsync();
                    items = fetched?.ToArray() ?? Array.Empty<CounterLibraryItem>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to fetch counter library items; using empty list for trigger resolution.");
                    items = Array.Empty<CounterLibraryItem>();
                }

                entry.Items = items;
                entry.ExpiresAt = now.Add(CounterLibraryCacheTtl);
                return entry.Items;
            }
            finally
            {
                entry.Gate.Release();
            }
        }

        private void TryAddTrigger(
            Dictionary<string, string> triggerToCounterId,
            string triggerToken,
            string counterId,
            string userId)
        {
            if (triggerToCounterId.TryGetValue(triggerToken, out var existing) && !string.Equals(existing, counterId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "⚠️ Custom counter trigger collision for user {UserId}: trigger '{Trigger}' maps to both '{Existing}' and '{Incoming}'. Keeping existing mapping.",
                    LogSanitizer.Sanitize(userId),
                    LogSanitizer.Sanitize(triggerToken),
                    LogSanitizer.Sanitize(existing),
                    LogSanitizer.Sanitize(counterId));
                return;
            }

            triggerToCounterId[triggerToken] = counterId;
        }

        private (ChatCommandDefinition? Config, int? InlineAmount) ResolveCommand(
            string commandText,
            ChatCommandConfiguration userConfig)
        {
            // 1. Try exact match
            if (userConfig.Commands.TryGetValue(commandText, out var userCmd)) return (userCmd, null);
            if (_defaultCommands.TryGetValue(commandText, out var defCmd)) return (defCmd, null);

            // 2. Try parsing inline amount like !d5+ or !death10-
            // Format: !<base><num><op> => resolve base command as !<base><op> with amount = <num>
            var inline = InlineAmountBeforeOpRegex.Match(commandText);
            if (inline.Success)
            {
                var basePart = inline.Groups[1].Value.ToLowerInvariant();
                var op = inline.Groups[3].Value;
                var baseCmd = $"{basePart}{op}";

                if (int.TryParse(inline.Groups[2].Value, out var amount) && amount >= 2 && amount <= 10)
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
