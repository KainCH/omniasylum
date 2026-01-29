using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Utilities;

namespace OmniForge.Infrastructure.Services
{
    public class GameCounterSetupService : IGameCounterSetupService
    {
        private readonly ICounterLibraryRepository _counterLibraryRepository;
        private readonly IGameCustomCountersConfigRepository _gameCustomCountersConfigRepository;
        private readonly IGameChatCommandsRepository _gameChatCommandsRepository;
        private readonly IGameCountersRepository _gameCountersRepository;
        private readonly IGameContextRepository _gameContextRepository;
        private readonly IUserRepository _userRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<GameCounterSetupService> _logger;

        public GameCounterSetupService(
            ICounterLibraryRepository counterLibraryRepository,
            IGameCustomCountersConfigRepository gameCustomCountersConfigRepository,
            IGameChatCommandsRepository gameChatCommandsRepository,
            IGameCountersRepository gameCountersRepository,
            IGameContextRepository gameContextRepository,
            IUserRepository userRepository,
            ICounterRepository counterRepository,
            IOverlayNotifier overlayNotifier,
            ILogger<GameCounterSetupService> logger)
        {
            _counterLibraryRepository = counterLibraryRepository;
            _gameCustomCountersConfigRepository = gameCustomCountersConfigRepository;
            _gameChatCommandsRepository = gameChatCommandsRepository;
            _gameCountersRepository = gameCountersRepository;
            _gameContextRepository = gameContextRepository;
            _userRepository = userRepository;
            _counterRepository = counterRepository;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public async Task AddLibraryCounterToGameAsync(string userId, string gameId, string counterId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(counterId))
            {
                return;
            }

            var safeUserId = userId!;

            var libraryItem = await _counterLibraryRepository.GetAsync(counterId);
            if (libraryItem == null)
            {
                throw new InvalidOperationException("Counter not found in library");
            }

            // Ensure per-game custom counter definition exists
            var gameCustomConfig = await _gameCustomCountersConfigRepository.GetAsync(safeUserId, gameId) ?? new CustomCounterConfiguration();
            gameCustomConfig.Counters ??= new Dictionary<string, CustomCounterDefinition>();

            if (!gameCustomConfig.Counters.ContainsKey(libraryItem.CounterId))
            {
                gameCustomConfig.Counters[libraryItem.CounterId] = new CustomCounterDefinition
                {
                    Name = libraryItem.Name,
                    Icon = libraryItem.Icon,
                    IncrementBy = Math.Max(1, libraryItem.IncrementBy),
                    DecrementBy = Math.Max(1, libraryItem.DecrementBy),
                    Milestones = (libraryItem.Milestones ?? Array.Empty<int>()).ToList()
                };

                await _gameCustomCountersConfigRepository.SaveAsync(safeUserId, gameId, gameCustomConfig);
            }

            // Ensure per-game chat commands exist for this counter
            var gameChatConfig = await _gameChatCommandsRepository.GetAsync(safeUserId, gameId) ?? new ChatCommandConfiguration();
            gameChatConfig.Commands ??= new Dictionary<string, ChatCommandDefinition>(StringComparer.OrdinalIgnoreCase);

            var defaultBase = $"!{libraryItem.CounterId}";
            var primaryBaseCommand = CommandNormalization.NormalizeBaseCommandOrDefault(libraryItem.LongCommand, defaultBase);
            var aliasBaseCommand = CommandNormalization.NormalizeBaseCommandOrEmpty(libraryItem.AliasCommand);
            if (!string.IsNullOrWhiteSpace(aliasBaseCommand)
                && string.Equals(aliasBaseCommand, primaryBaseCommand, StringComparison.OrdinalIgnoreCase))
            {
                aliasBaseCommand = string.Empty;
            }

            var baseCommands = new List<string> { primaryBaseCommand };
            if (!string.IsNullOrWhiteSpace(aliasBaseCommand))
            {
                baseCommands.Add(aliasBaseCommand);
            }

            foreach (var baseCommand in baseCommands)
            {
                var incCommand = $"{baseCommand}+";
                var decCommand = $"{baseCommand}-";

                if (!gameChatConfig.Commands.ContainsKey(baseCommand))
                {
                    gameChatConfig.Commands[baseCommand] = new ChatCommandDefinition
                    {
                        Response = $"{libraryItem.Name}: {{{{{libraryItem.CounterId}}}}}",
                        Permission = "everyone",
                        Cooldown = 5,
                        Enabled = true
                    };
                }

                if (!gameChatConfig.Commands.ContainsKey(incCommand))
                {
                    gameChatConfig.Commands[incCommand] = new ChatCommandDefinition
                    {
                        Action = "increment",
                        Counter = libraryItem.CounterId,
                        Permission = "moderator",
                        Cooldown = 1,
                        Enabled = true
                    };
                }

                if (!gameChatConfig.Commands.ContainsKey(decCommand))
                {
                    gameChatConfig.Commands[decCommand] = new ChatCommandDefinition
                    {
                        Action = "decrement",
                        Counter = libraryItem.CounterId,
                        Permission = "moderator",
                        Cooldown = 1,
                        Enabled = true
                    };
                }
            }

            await _gameChatCommandsRepository.SaveAsync(safeUserId, gameId, gameChatConfig);

            // Ensure per-game counter values include the key
            try
            {
                var counters = await _gameCountersRepository.GetAsync(safeUserId, gameId) ?? new Counter { TwitchUserId = safeUserId };
                counters.CustomCounters ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (!counters.CustomCounters.ContainsKey(libraryItem.CounterId))
                {
                    counters.CustomCounters[libraryItem.CounterId] = 0;
                    counters.LastUpdated = DateTimeOffset.UtcNow;
                    await _gameCountersRepository.SaveAsync(safeUserId, gameId, counters);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed ensuring per-game counter value for user {UserId} game {GameId}",
                    (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            // If this game is active, also update active configs so bot/overlay pick it up immediately
            try
            {
                var ctx = await _gameContextRepository.GetAsync(safeUserId!);
                var isActive = ctx != null && string.Equals(ctx.ActiveGameId, gameId, StringComparison.OrdinalIgnoreCase);
                if (isActive)
                {
                    await _counterRepository.SaveCustomCountersConfigAsync(safeUserId!, gameCustomConfig);
                    await _userRepository.SaveChatCommandsConfigAsync(safeUserId!, gameChatConfig);

                    await _overlayNotifier.NotifyCustomAlertAsync(safeUserId!, "customCountersUpdated", new { counters = gameCustomConfig.Counters });
                    await _overlayNotifier.NotifyCustomAlertAsync(safeUserId!, "chatCommandsUpdated", new { commands = gameChatConfig.Commands });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed updating active config for user {UserId}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            _logger.LogInformation("✅ Added library counter {CounterId} to game {GameId} for user {UserId}",
                (counterId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
        }

        public async Task RemoveLibraryCounterFromGameAsync(string userId, string gameId, string counterId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(counterId))
            {
                return;
            }

            var safeUserId = userId!;

            var normalizedCounterId = counterId.Trim();
            var libraryItem = await _counterLibraryRepository.GetAsync(normalizedCounterId);

            // Remove from per-game custom counter definitions
            var gameCustomConfig = await _gameCustomCountersConfigRepository.GetAsync(safeUserId, gameId) ?? new CustomCounterConfiguration();
            gameCustomConfig.Counters ??= new Dictionary<string, CustomCounterDefinition>();

            var hadCustomCounter = gameCustomConfig.Counters.Remove(normalizedCounterId);
            if (hadCustomCounter)
            {
                await _gameCustomCountersConfigRepository.SaveAsync(safeUserId, gameId, gameCustomConfig);
            }

            // Remove from per-game chat commands
            var gameChatConfig = await _gameChatCommandsRepository.GetAsync(safeUserId, gameId) ?? new ChatCommandConfiguration();
            gameChatConfig.Commands ??= new Dictionary<string, ChatCommandDefinition>(StringComparer.OrdinalIgnoreCase);

            // Prefer removal based on the library's configured commands if available.
            if (libraryItem != null)
            {
                var defaultBase = $"!{libraryItem.CounterId}";
                var primaryBaseCommand = CommandNormalization.NormalizeBaseCommandOrDefault(libraryItem.LongCommand, defaultBase);
                var aliasBaseCommand = CommandNormalization.NormalizeBaseCommandOrEmpty(libraryItem.AliasCommand);
                if (!string.IsNullOrWhiteSpace(aliasBaseCommand)
                    && string.Equals(aliasBaseCommand, primaryBaseCommand, StringComparison.OrdinalIgnoreCase))
                {
                    aliasBaseCommand = string.Empty;
                }

                var baseCommands = new List<string> { primaryBaseCommand };
                if (!string.IsNullOrWhiteSpace(aliasBaseCommand))
                {
                    baseCommands.Add(aliasBaseCommand);
                }

                foreach (var baseCommand in baseCommands)
                {
                    var incCommand = $"{baseCommand}+";
                    var decCommand = $"{baseCommand}-";

                    gameChatConfig.Commands.Remove(baseCommand);
                    gameChatConfig.Commands.Remove(incCommand);
                    gameChatConfig.Commands.Remove(decCommand);
                }
            }

            // Also remove any increment/decrement commands that target this counterId (covers renamed keys).
            var keysToRemove = gameChatConfig.Commands
                .Where(kvp =>
                    (string.Equals(kvp.Value.Action, "increment", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(kvp.Value.Action, "decrement", StringComparison.OrdinalIgnoreCase))
                    && string.Equals(kvp.Value.Counter, normalizedCounterId, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                gameChatConfig.Commands.Remove(key);
            }

            await _gameChatCommandsRepository.SaveAsync(safeUserId, gameId, gameChatConfig);

            // Remove counter value key from per-game counters
            try
            {
                var counters = await _gameCountersRepository.GetAsync(safeUserId, gameId);
                if (counters?.CustomCounters != null && counters.CustomCounters.Remove(normalizedCounterId))
                {
                    counters.LastUpdated = DateTimeOffset.UtcNow;
                    await _gameCountersRepository.SaveAsync(safeUserId, gameId, counters);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed removing per-game counter value for user {UserId} game {GameId}",
                    (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            // If this game is active, also update active configs so bot/overlay pick it up immediately
            try
            {
                var ctx = await _gameContextRepository.GetAsync(safeUserId!);
                var isActive = ctx != null && string.Equals(ctx.ActiveGameId, gameId, StringComparison.OrdinalIgnoreCase);
                if (isActive)
                {
                    await _counterRepository.SaveCustomCountersConfigAsync(safeUserId!, gameCustomConfig);
                    await _userRepository.SaveChatCommandsConfigAsync(safeUserId!, gameChatConfig);

                    await _overlayNotifier.NotifyCustomAlertAsync(safeUserId!, "customCountersUpdated", new { counters = gameCustomConfig.Counters });
                    await _overlayNotifier.NotifyCustomAlertAsync(safeUserId!, "chatCommandsUpdated", new { commands = gameChatConfig.Commands });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed updating active config after removing counter for user {UserId}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            _logger.LogInformation("✅ Removed library counter {CounterId} from game {GameId} for user {UserId}",
                (counterId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
        }

    }
}
