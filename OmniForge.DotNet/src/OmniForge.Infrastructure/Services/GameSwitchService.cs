using System;
using System.Collections.Generic;
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
        private readonly IGameCoreCountersConfigRepository _gameCoreCountersConfigRepository;
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
            IGameCoreCountersConfigRepository gameCoreCountersConfigRepository,
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
            _gameCoreCountersConfigRepository = gameCoreCountersConfigRepository;
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

            User? user = null;
            try
            {
                user = await _userRepository.GetUserAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed reading user for {UserId}", LogSanitizer.Sanitize(userId));
            }

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

                        // Persist core counter selection (overlay visibility) per game
                        if (user?.OverlaySettings?.Counters != null)
                        {
                            var counters = user.OverlaySettings.Counters;
                            await _gameCoreCountersConfigRepository.SaveAsync(
                                userId,
                                current.ActiveGameId!,
                                new GameCoreCountersConfig(
                                    UserId: userId,
                                    GameId: current.ActiveGameId!,
                                    DeathsEnabled: counters.Deaths,
                                    SwearsEnabled: counters.Swears,
                                    ScreamsEnabled: counters.Screams,
                                    BitsEnabled: counters.Bits,
                                    UpdatedAt: now));
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

            // Load per-game core counter selection (seed from current user's overlay settings if missing)
            GameCoreCountersConfig? coreSelection = null;
            try
            {
                coreSelection = await _gameCoreCountersConfigRepository.GetAsync(userId, gameId);
                if (coreSelection == null)
                {
                    var overlayCounters = user?.OverlaySettings?.Counters;
                    coreSelection = new GameCoreCountersConfig(
                        UserId: userId,
                        GameId: gameId,
                        DeathsEnabled: overlayCounters?.Deaths ?? true,
                        SwearsEnabled: overlayCounters?.Swears ?? true,
                        ScreamsEnabled: overlayCounters?.Screams ?? true,
                        BitsEnabled: overlayCounters?.Bits ?? false,
                        UpdatedAt: now);

                    await _gameCoreCountersConfigRepository.SaveAsync(userId, gameId, coreSelection);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed loading core counter selection for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
            }

            // Apply core selection to overlay visibility and (by override) to default chat commands
            try
            {
                if (user != null && coreSelection != null)
                {
                    user.OverlaySettings ??= new OverlaySettings();
                    user.OverlaySettings.Counters ??= new OverlayCounters();
                    user.OverlaySettings.Counters.Deaths = coreSelection.DeathsEnabled;
                    user.OverlaySettings.Counters.Swears = coreSelection.SwearsEnabled;
                    user.OverlaySettings.Counters.Screams = coreSelection.ScreamsEnabled;
                    user.OverlaySettings.Counters.Bits = coreSelection.BitsEnabled;

                    await _userRepository.SaveUserAsync(user);
                    await _overlayNotifier.NotifySettingsUpdateAsync(userId, user.OverlaySettings);
                }

                ApplyCoreSelectionToChatCommands(newChatCommands, coreSelection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed applying core counter selection for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
            }

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

        public async Task ApplyActiveCoreCountersSelectionAsync(string userId, string gameId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            try
            {
                var ctx = await _gameContextRepository.GetAsync(userId);
                if (ctx == null || !string.Equals(ctx.ActiveGameId, gameId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var selection = await _gameCoreCountersConfigRepository.GetAsync(userId, gameId);
                if (selection == null)
                {
                    return;
                }

                User? user = null;
                try
                {
                    user = await _userRepository.GetUserAsync(userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed reading user for {UserId}", LogSanitizer.Sanitize(userId));
                }

                if (user != null)
                {
                    user.OverlaySettings ??= new OverlaySettings();
                    user.OverlaySettings.Counters ??= new OverlayCounters();
                    user.OverlaySettings.Counters.Deaths = selection.DeathsEnabled;
                    user.OverlaySettings.Counters.Swears = selection.SwearsEnabled;
                    user.OverlaySettings.Counters.Screams = selection.ScreamsEnabled;
                    user.OverlaySettings.Counters.Bits = selection.BitsEnabled;

                    await _userRepository.SaveUserAsync(user);
                    await _overlayNotifier.NotifySettingsUpdateAsync(userId, user.OverlaySettings);
                }

                ChatCommandConfiguration activeChat;
                try
                {
                    activeChat = await _userRepository.GetChatCommandsConfigAsync(userId) ?? new ChatCommandConfiguration();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed reading active chat commands for user {UserId}", LogSanitizer.Sanitize(userId));
                    activeChat = new ChatCommandConfiguration();
                }

                ApplyCoreSelectionToChatCommands(activeChat, selection);
                await _userRepository.SaveChatCommandsConfigAsync(userId, activeChat);
                await _overlayNotifier.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", new { commands = activeChat.Commands });

                _logger.LogInformation("✅ Applied active core counter selection for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed applying active core counter selection for user {UserId} game {GameId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(gameId));
            }
        }

        private static void ApplyCoreSelectionToChatCommands(ChatCommandConfiguration chatCommands, GameCoreCountersConfig? selection)
        {
            if (chatCommands == null || selection == null) return;

            chatCommands.Commands ??= new Dictionary<string, ChatCommandDefinition>(StringComparer.OrdinalIgnoreCase);

            SetCoreCommandEnabled(chatCommands, "!deaths", selection.DeathsEnabled);
            SetCoreCommandEnabled(chatCommands, "!death+", selection.DeathsEnabled);
            SetCoreCommandEnabled(chatCommands, "!death-", selection.DeathsEnabled);
            SetCoreCommandEnabled(chatCommands, "!d+", selection.DeathsEnabled);
            SetCoreCommandEnabled(chatCommands, "!d-", selection.DeathsEnabled);

            SetCoreCommandEnabled(chatCommands, "!swears", selection.SwearsEnabled);
            SetCoreCommandEnabled(chatCommands, "!sw", selection.SwearsEnabled);
            SetCoreCommandEnabled(chatCommands, "!swear+", selection.SwearsEnabled);
            SetCoreCommandEnabled(chatCommands, "!swear-", selection.SwearsEnabled);
            SetCoreCommandEnabled(chatCommands, "!sw+", selection.SwearsEnabled);
            SetCoreCommandEnabled(chatCommands, "!sw-", selection.SwearsEnabled);

            SetCoreCommandEnabled(chatCommands, "!screams", selection.ScreamsEnabled);
            SetCoreCommandEnabled(chatCommands, "!sc", selection.ScreamsEnabled);
            SetCoreCommandEnabled(chatCommands, "!scream+", selection.ScreamsEnabled);
            SetCoreCommandEnabled(chatCommands, "!scream-", selection.ScreamsEnabled);
            SetCoreCommandEnabled(chatCommands, "!sc+", selection.ScreamsEnabled);
            SetCoreCommandEnabled(chatCommands, "!sc-", selection.ScreamsEnabled);
        }

        private static void SetCoreCommandEnabled(ChatCommandConfiguration config, string command, bool enabled)
        {
            if (enabled)
            {
                // Remove override so defaults can run.
                if (config.Commands.ContainsKey(command))
                {
                    config.Commands.Remove(command);
                }

                return;
            }

            // Add an override that disables the command (overrides defaults in ChatCommandProcessor).
            config.Commands[command] = new ChatCommandDefinition
            {
                Enabled = false,
                Cooldown = 0,
                Permission = "everyone",
                Response = string.Empty
            };
        }
    }
}
