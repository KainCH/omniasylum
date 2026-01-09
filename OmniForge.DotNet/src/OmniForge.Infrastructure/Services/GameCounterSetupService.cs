using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

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

            var libraryItem = await _counterLibraryRepository.GetAsync(counterId);
            if (libraryItem == null)
            {
                throw new InvalidOperationException("Counter not found in library");
            }

            // Ensure per-game custom counter definition exists
            var gameCustomConfig = await _gameCustomCountersConfigRepository.GetAsync(userId, gameId) ?? new CustomCounterConfiguration();
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

                await _gameCustomCountersConfigRepository.SaveAsync(userId, gameId, gameCustomConfig);
            }

            // Ensure per-game chat commands exist for this counter
            var gameChatConfig = await _gameChatCommandsRepository.GetAsync(userId, gameId) ?? new ChatCommandConfiguration();
            gameChatConfig.Commands ??= new Dictionary<string, ChatCommandDefinition>(StringComparer.OrdinalIgnoreCase);

            var baseCommand = $"!{libraryItem.CounterId}";
            var incCommand = $"!{libraryItem.CounterId}+";
            var decCommand = $"!{libraryItem.CounterId}-";

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

            await _gameChatCommandsRepository.SaveAsync(userId, gameId, gameChatConfig);

            // Ensure per-game counter values include the key
            try
            {
                var counters = await _gameCountersRepository.GetAsync(userId, gameId) ?? new Counter { TwitchUserId = userId };
                counters.CustomCounters ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (!counters.CustomCounters.ContainsKey(libraryItem.CounterId))
                {
                    counters.CustomCounters[libraryItem.CounterId] = 0;
                    counters.LastUpdated = DateTimeOffset.UtcNow;
                    await _gameCountersRepository.SaveAsync(userId, gameId, counters);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed ensuring per-game counter value for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
            }

            // If this game is active, also update active configs so bot/overlay pick it up immediately
            try
            {
                var ctx = await _gameContextRepository.GetAsync(userId);
                var isActive = ctx != null && string.Equals(ctx.ActiveGameId, gameId, StringComparison.OrdinalIgnoreCase);
                if (isActive)
                {
                    await _counterRepository.SaveCustomCountersConfigAsync(userId, gameCustomConfig);
                    await _userRepository.SaveChatCommandsConfigAsync(userId, gameChatConfig);

                    await _overlayNotifier.NotifyCustomAlertAsync(userId, "customCountersUpdated", new { counters = gameCustomConfig.Counters });
                    await _overlayNotifier.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", new { commands = gameChatConfig.Commands });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed updating active config for user {UserId}", LogSanitizer.Sanitize(userId));
            }

            _logger.LogInformation("✅ Added library counter {CounterId} to game {GameId} for user {UserId}", LogSanitizer.Sanitize(counterId), LogSanitizer.Sanitize(gameId), LogSanitizer.Sanitize(userId));
        }
    }
}
