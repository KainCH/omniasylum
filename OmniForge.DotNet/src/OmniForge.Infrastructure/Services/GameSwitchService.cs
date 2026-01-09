using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services
{
    public class GameSwitchService : IGameSwitchService
    {
        private readonly IGameContextRepository _gameContextRepository;
        private readonly IGameCountersRepository _gameCountersRepository;
        private readonly IGameLibraryRepository _gameLibraryRepository;
        private readonly IGameChatCommandsRepository _gameChatCommandsRepository;
        private readonly IGameCustomCountersConfigRepository _gameCustomCountersConfigRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly IUserRepository _userRepository;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<GameSwitchService> _logger;

        public GameSwitchService(
            IGameContextRepository gameContextRepository,
            IGameCountersRepository gameCountersRepository,
            IGameLibraryRepository gameLibraryRepository,
            IGameChatCommandsRepository gameChatCommandsRepository,
            IGameCustomCountersConfigRepository gameCustomCountersConfigRepository,
            ICounterRepository counterRepository,
            IUserRepository userRepository,
            IOverlayNotifier overlayNotifier,
            ILogger<GameSwitchService> logger)
        {
            _gameContextRepository = gameContextRepository;
            _gameCountersRepository = gameCountersRepository;
            _gameLibraryRepository = gameLibraryRepository;
            _gameChatCommandsRepository = gameChatCommandsRepository;
            _gameCustomCountersConfigRepository = gameCustomCountersConfigRepository;
            _counterRepository = counterRepository;
            _userRepository = userRepository;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public async Task HandleGameDetectedAsync(string userId, string gameId, string gameName, string? boxArtUrl = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            var current = await _gameContextRepository.GetAsync(userId);
            if (current != null && string.Equals(current.ActiveGameId, gameId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;

            // Capture current active configs (these are what the bot/overlay are using right now)
            ChatCommandConfiguration? activeChatCommands = null;
            CustomCounterConfiguration? activeCustomCounters = null;

            try
            {
                activeChatCommands = await _userRepository.GetChatCommandsConfigAsync(userId) ?? new ChatCommandConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed reading active chat commands for user {UserId}", LogSanitizer.Sanitize(userId));
                activeChatCommands = new ChatCommandConfiguration();
            }

            try
            {
                activeCustomCounters = await _counterRepository.GetCustomCountersConfigAsync(userId) ?? new CustomCounterConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed reading active custom counters config for user {UserId}", LogSanitizer.Sanitize(userId));
                activeCustomCounters = new CustomCounterConfiguration();
            }

            // Save current counters to previous game (if we know what it was)
            try
            {
                if (!string.IsNullOrWhiteSpace(current?.ActiveGameId))
                {
                    var existing = await _counterRepository.GetCountersAsync(userId) ?? new Counter { TwitchUserId = userId, LastUpdated = now };
                    existing.LastUpdated = now;
                    await _gameCountersRepository.SaveAsync(userId, current.ActiveGameId!, existing);
                    _logger.LogInformation("💾 Saved counters for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(current.ActiveGameId!));

                    // Persist current active configs to the previous game as well
                    try
                    {
                        if (activeChatCommands != null)
                        {
                            await _gameChatCommandsRepository.SaveAsync(userId, current.ActiveGameId!, activeChatCommands);
                        }

                        if (activeCustomCounters != null)
                        {
                            await _gameCustomCountersConfigRepository.SaveAsync(userId, current.ActiveGameId!, activeCustomCounters);
                        }

                        _logger.LogInformation("💾 Saved per-game configs for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(current.ActiveGameId!));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed saving per-game configs for previous game for user {UserId}", LogSanitizer.Sanitize(userId));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed saving counters for previous game for user {UserId}", LogSanitizer.Sanitize(userId));
            }

            // Ensure game exists in library
            try
            {
                await _gameLibraryRepository.UpsertAsync(new GameLibraryItem
                {
                    UserId = userId,
                    GameId = gameId,
                    GameName = gameName ?? string.Empty,
                    BoxArtUrl = boxArtUrl ?? string.Empty,
                    CreatedAt = now,
                    LastSeenAt = now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed upserting game library item for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
            }

            // Load counters for the new game (or initialize)
            Counter newCounters;
            try
            {
                var loaded = await _gameCountersRepository.GetAsync(userId, gameId);
                newCounters = loaded ?? new Counter
                {
                    TwitchUserId = userId,
                    Deaths = 0,
                    Swears = 0,
                    Screams = 0,
                    Bits = 0,
                    LastUpdated = now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed loading game counters for user {UserId} game {GameId}; using defaults", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
                newCounters = new Counter { TwitchUserId = userId, LastUpdated = now };
            }

            // Load per-game configs (seed from active if missing)
            ChatCommandConfiguration newChatCommands;
            try
            {
                var loadedChat = await _gameChatCommandsRepository.GetAsync(userId, gameId);
                newChatCommands = loadedChat ?? activeChatCommands ?? new ChatCommandConfiguration();
                if (loadedChat == null)
                {
                    await _gameChatCommandsRepository.SaveAsync(userId, gameId, newChatCommands);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed loading game chat commands for user {UserId} game {GameId}; using active", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
                newChatCommands = activeChatCommands ?? new ChatCommandConfiguration();
            }

            CustomCounterConfiguration newCustomCountersConfig;
            try
            {
                var loadedCustom = await _gameCustomCountersConfigRepository.GetAsync(userId, gameId);
                newCustomCountersConfig = loadedCustom ?? activeCustomCounters ?? new CustomCounterConfiguration();
                if (loadedCustom == null)
                {
                    await _gameCustomCountersConfigRepository.SaveAsync(userId, gameId, newCustomCountersConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed loading game custom counters config for user {UserId} game {GameId}; using active", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
                newCustomCountersConfig = activeCustomCounters ?? new CustomCounterConfiguration();
            }

            newCounters.TwitchUserId = userId;
            newCounters.LastUpdated = now;

            // Swap the active counters (existing system uses the primary counters row)
            await _counterRepository.SaveCountersAsync(newCounters);

            // Swap the active per-user configs so chat + overlay use the game-scoped setup
            try
            {
                await _userRepository.SaveChatCommandsConfigAsync(userId, newChatCommands);
                await _counterRepository.SaveCustomCountersConfigAsync(userId, newCustomCountersConfig);
                await _overlayNotifier.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", new { commands = newChatCommands.Commands });
                await _overlayNotifier.NotifyCustomAlertAsync(userId, "customCountersUpdated", new { counters = newCustomCountersConfig.Counters });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed applying active per-game configs for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
            }

            await _gameContextRepository.SaveAsync(new GameContext
            {
                UserId = userId,
                ActiveGameId = gameId,
                ActiveGameName = gameName,
                UpdatedAt = now
            });

            await _overlayNotifier.NotifyCounterUpdateAsync(userId, newCounters);
            _logger.LogInformation("🔄 Active game switched for user {UserId}: {GameId} ({GameName})", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId), LogSanitizer.Sanitize(gameName));
        }
    }
}
