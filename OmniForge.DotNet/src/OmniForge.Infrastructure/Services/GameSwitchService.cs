using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

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
        private readonly ITwitchApiService _twitchApiService;
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
            ITwitchApiService twitchApiService,
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
            _twitchApiService = twitchApiService;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public async Task HandleGameDetectedAsync(string userId, string gameId, string gameName, string? boxArtUrl = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            var safeUserId = userId!;
            var safeGameId = gameId!;
            var safeGameName = gameName ?? string.Empty;

            var current = await _gameContextRepository.GetAsync(userId);
            var now = DateTimeOffset.UtcNow;

            GameLibraryItem? existingLibraryItem = null;
            GameLibraryItem? upsertedLibraryItem = null;

            // If the detected game matches the current active game, we still want to ensure it exists
            // in the game library and that a per-game core counter selection exists. This helps recover
            // from restart/seed scenarios where the context exists but configs/library entries do not.
            if (current != null && string.Equals(current.ActiveGameId, gameId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    existingLibraryItem = await _gameLibraryRepository.GetAsync(userId, gameId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "❌ Failed reading existing game library item for user {UserId} game {GameId}",
                        userId.Replace("\r", "\\r").Replace("\n", "\\n"),
                        gameId.Replace("\r", "\\r").Replace("\n", "\\n"));
                }

                GameCoreCountersConfig? existingSelection = null;
                try
                {
                    existingSelection = await _gameCoreCountersConfigRepository.GetAsync(userId, gameId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "❌ Failed reading core counter selection for user {UserId} game {GameId}",
                        userId.Replace("\r", "\\r").Replace("\n", "\\n"),
                        gameId.Replace("\r", "\\r").Replace("\n", "\\n"));
                }

                // Everything is already set; keep the original early-return behavior.
                if (existingLibraryItem != null && existingSelection != null)
                {
                    return;
                }

                try
                {
                    if (existingLibraryItem == null)
                    {
                        upsertedLibraryItem = new GameLibraryItem
                        {
                            UserId = userId,
                            GameId = gameId,
                            GameName = gameName ?? current.ActiveGameName ?? string.Empty,
                            BoxArtUrl = boxArtUrl ?? string.Empty,
                            CreatedAt = now,
                            LastSeenAt = now,
                            EnabledContentClassificationLabels = null
                        };

                        await _gameLibraryRepository.UpsertAsync(upsertedLibraryItem);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "❌ Failed upserting game library item for user {UserId} game {GameId}",
                        userId.Replace("\r", "\\r").Replace("\n", "\\n"),
                        gameId.Replace("\r", "\\r").Replace("\n", "\\n"));
                }

                try
                {
                    if (existingSelection == null)
                    {
                        User? userForSeed = null;
                        try
                        {
                            userForSeed = await _userRepository.GetUserAsync(userId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "❌ Failed reading user for {UserId}",
                                userId.Replace("\r", "\\r").Replace("\n", "\\n"));
                        }

                        var overlayCounters = userForSeed?.OverlaySettings?.Counters;
                        var selection = new GameCoreCountersConfig(
                            UserId: userId,
                            GameId: gameId,
                            DeathsEnabled: overlayCounters?.Deaths ?? true,
                            SwearsEnabled: overlayCounters?.Swears ?? true,
                            ScreamsEnabled: overlayCounters?.Screams ?? true,
                            BitsEnabled: overlayCounters?.Bits ?? false,
                            UpdatedAt: now);

                        await _gameCoreCountersConfigRepository.SaveAsync(userId, gameId, selection);

                        // Apply once so overlay/chat pick it up immediately.
                        await ApplyActiveCoreCountersSelectionAsync(userId, gameId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "❌ Failed seeding core counter selection for user {UserId} game {GameId}",
                        userId.Replace("\r", "\\r").Replace("\n", "\\n"),
                        gameId.Replace("\r", "\\r").Replace("\n", "\\n"));
                }

                return;
            }

            User? user = null;
            try
            {
                user = await _userRepository.GetUserAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed reading user for {UserId}",
                    userId.Replace("\r", "\\r").Replace("\n", "\\n"));
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
                _logger.LogError(
                    ex,
                    "❌ Failed reading active chat commands for user {UserId}",
                    userId.Replace("\r", "\\r").Replace("\n", "\\n"));
                activeChatCommands = new ChatCommandConfiguration();
            }

            try
            {
                activeCustomCounters = await _counterRepository.GetCustomCountersConfigAsync(userId) ?? new CustomCounterConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed reading active custom counters config for user {UserId}",
                    userId.Replace("\r", "\\r").Replace("\n", "\\n"));
                activeCustomCounters = new CustomCounterConfiguration();
            }

            // Save current counters to previous game (if we know what it was)
            try
            {
                if (!string.IsNullOrWhiteSpace(current?.ActiveGameId))
                {
                    var existing = await _counterRepository.GetCountersAsync(safeUserId) ?? new Counter { TwitchUserId = safeUserId, LastUpdated = now };
                    existing.LastUpdated = now;
                    existing.LastCategoryName = current.ActiveGameName;
                    await _gameCountersRepository.SaveAsync(safeUserId, current.ActiveGameId!, existing);
                    _logger.LogInformation(
                        "💾 Saved counters for user {UserId} game {GameId}",
                        safeUserId.Replace("\r", "\\r").Replace("\n", "\\n"),
                        current.ActiveGameId!.Replace("\r", "\\r").Replace("\n", "\\n"));

                    // Persist current active configs to the previous game as well
                    try
                    {
                        if (activeChatCommands != null)
                        {
                            await _gameChatCommandsRepository.SaveAsync(safeUserId, current.ActiveGameId!, activeChatCommands);
                        }

                        if (activeCustomCounters != null)
                        {
                            await _gameCustomCountersConfigRepository.SaveAsync(safeUserId, current.ActiveGameId!, activeCustomCounters);
                        }

                        // Persist core counter selection (overlay visibility) per game
                        if (user?.OverlaySettings?.Counters != null)
                        {
                            var counters = user.OverlaySettings.Counters;
                            await _gameCoreCountersConfigRepository.SaveAsync(
                                safeUserId,
                                current.ActiveGameId!,
                                new GameCoreCountersConfig(
                                    UserId: safeUserId,
                                    GameId: current.ActiveGameId!,
                                    DeathsEnabled: counters.Deaths,
                                    SwearsEnabled: counters.Swears,
                                    ScreamsEnabled: counters.Screams,
                                    BitsEnabled: counters.Bits,
                                    UpdatedAt: now));
                        }

                        _logger.LogInformation(
                            "💾 Saved per-game configs for user {UserId} game {GameId}",
                            (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                            (current.ActiveGameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "❌ Failed saving per-game configs for previous game for user {UserId}",
                            (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed saving counters for previous game for user {UserId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            // Ensure game exists in library
            try
            {
                existingLibraryItem = await _gameLibraryRepository.GetAsync(safeUserId, safeGameId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed reading existing game library item for user {UserId} game {GameId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            try
            {
                upsertedLibraryItem = new GameLibraryItem
                {
                    UserId = safeUserId,
                    GameId = safeGameId,
                    GameName = safeGameName,
                    BoxArtUrl = boxArtUrl ?? string.Empty,
                    CreatedAt = existingLibraryItem?.CreatedAt ?? now,
                    LastSeenAt = now,
                    EnabledContentClassificationLabels = existingLibraryItem?.EnabledContentClassificationLabels
                };

                await _gameLibraryRepository.UpsertAsync(upsertedLibraryItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed upserting game library item for user {UserId} game {GameId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            // Load counters for the new game (or initialize)
            Counter newCounters;
            try
            {
                var loaded = await _gameCountersRepository.GetAsync(safeUserId, safeGameId);
                newCounters = loaded ?? new Counter
                {
                    TwitchUserId = safeUserId,
                    Deaths = 0,
                    Swears = 0,
                    Screams = 0,
                    Bits = 0,
                    LastUpdated = now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed loading game counters for user {UserId} game {GameId}; using defaults",
                    safeUserId.Replace("\r", "\\r").Replace("\n", "\\n"),
                    safeGameId.Replace("\r", "\\r").Replace("\n", "\\n"));
                newCounters = new Counter { TwitchUserId = safeUserId, LastUpdated = now };
            }

            // Load per-game configs (seed from active if missing)
            ChatCommandConfiguration newChatCommands;
            try
            {
                var loadedChat = await _gameChatCommandsRepository.GetAsync(safeUserId, safeGameId);
                newChatCommands = loadedChat ?? activeChatCommands ?? new ChatCommandConfiguration();
                if (loadedChat == null)
                {
                    await _gameChatCommandsRepository.SaveAsync(safeUserId, safeGameId, newChatCommands);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed loading game chat commands for user {UserId} game {GameId}; using active",
                    safeUserId.Replace("\r", "\\r").Replace("\n", "\\n"),
                    safeGameId.Replace("\r", "\\r").Replace("\n", "\\n"));
                newChatCommands = activeChatCommands ?? new ChatCommandConfiguration();
            }

            CustomCounterConfiguration newCustomCountersConfig;
            try
            {
                var loadedCustom = await _gameCustomCountersConfigRepository.GetAsync(safeUserId, safeGameId);

                // IMPORTANT: Do not seed a newly detected game's custom counters from the currently-active game.
                // Otherwise, switching/adding a game can cause counters from the previous game to appear as
                // enabled for the new game.
                newCustomCountersConfig = loadedCustom ?? new CustomCounterConfiguration();

                if (loadedCustom == null)
                {
                    await _gameCustomCountersConfigRepository.SaveAsync(safeUserId, safeGameId, newCustomCountersConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed loading game custom counters config for user {UserId} game {GameId}; using empty config",
                    safeUserId.Replace("\r", "\\r").Replace("\n", "\\n"),
                    safeGameId.Replace("\r", "\\r").Replace("\n", "\\n"));
                newCustomCountersConfig = new CustomCounterConfiguration();
            }

            newCounters.TwitchUserId = safeUserId;
            newCounters.LastUpdated = now;
            newCounters.LastCategoryName = safeGameName;

            // Load per-game core counter selection (seed from current user's overlay settings if missing)
            GameCoreCountersConfig? coreSelection = null;
            try
            {
                coreSelection = await _gameCoreCountersConfigRepository.GetAsync(safeUserId, safeGameId);
                if (coreSelection == null)
                {
                    var overlayCounters = user?.OverlaySettings?.Counters;
                    coreSelection = new GameCoreCountersConfig(
                        UserId: safeUserId,
                        GameId: safeGameId,
                        DeathsEnabled: overlayCounters?.Deaths ?? true,
                        SwearsEnabled: overlayCounters?.Swears ?? true,
                        ScreamsEnabled: overlayCounters?.Screams ?? true,
                        BitsEnabled: overlayCounters?.Bits ?? false,
                        UpdatedAt: now);

                    await _gameCoreCountersConfigRepository.SaveAsync(safeUserId, safeGameId, coreSelection);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed loading core counter selection for user {UserId} game {GameId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
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
                    await _overlayNotifier.NotifySettingsUpdateAsync(safeUserId, user.OverlaySettings);
                }

                ApplyCoreSelectionToChatCommands(newChatCommands, coreSelection);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed applying core counter selection for user {UserId} game {GameId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            // Swap the active counters (existing system uses the primary counters row)
            await _counterRepository.SaveCountersAsync(newCounters);

            // Swap the active per-user configs so chat + overlay use the game-scoped setup
            try
            {
                await _userRepository.SaveChatCommandsConfigAsync(safeUserId, newChatCommands);
                await _counterRepository.SaveCustomCountersConfigAsync(safeUserId, newCustomCountersConfig);
                await _overlayNotifier.NotifyCustomAlertAsync(safeUserId, "chatCommandsUpdated", new { commands = newChatCommands.Commands });
                await _overlayNotifier.NotifyCustomAlertAsync(safeUserId, "customCountersUpdated", new { counters = newCustomCountersConfig.Counters });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed applying active per-game configs for user {UserId} game {GameId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            await _gameContextRepository.SaveAsync(new GameContext
            {
                UserId = safeUserId,
                ActiveGameId = safeGameId,
                ActiveGameName = safeGameName,
                UpdatedAt = now
            });

            // Apply per-game Content Classification Labels to the channel when a new game is detected.
            try
            {
                if (upsertedLibraryItem?.EnabledContentClassificationLabels != null)
                {
                    await _twitchApiService.UpdateChannelInformationAsync(safeUserId, safeGameId, upsertedLibraryItem.EnabledContentClassificationLabels);
                }
                else if (user?.Features?.StreamSettings?.DefaultContentClassificationLabels != null)
                {
                    var fallback = user.Features.StreamSettings.DefaultContentClassificationLabels;
                    _logger.LogInformation(
                        "🏷️ Using user default CCL fallback (game has no admin CCL config). user_id={UserId} game_id={GameId} enabled_ccls={Ccls}",
                        (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                        (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                        string.Join(", ", fallback.Select(v => (v ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"))));

                    await _twitchApiService.UpdateChannelInformationAsync(safeUserId, safeGameId, fallback);
                }
                else
                {
                    _logger.LogInformation(
                        "ℹ️ No admin CCL config and no user default CCL fallback; skipping CCL apply. user_id={UserId} game_id={GameId}",
                        (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                        (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed updating Twitch channel info (CCLs) for user {UserId} game {GameId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }

            await _overlayNotifier.NotifyCounterUpdateAsync(safeUserId, newCounters);
            _logger.LogInformation(
                "🔄 Active game switched for user {UserId}: {GameId} ({GameName})",
                (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                (gameName ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
        }

        public async Task ApplyActiveCoreCountersSelectionAsync(string userId, string gameId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            var safeUserId = userId!;

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
                    _logger.LogError(
                        ex,
                        "❌ Failed reading user for {UserId}",
                        (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
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
                    await _overlayNotifier.NotifySettingsUpdateAsync(safeUserId, user.OverlaySettings);
                }

                ChatCommandConfiguration activeChat;
                try
                {
                    activeChat = await _userRepository.GetChatCommandsConfigAsync(safeUserId) ?? new ChatCommandConfiguration();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "❌ Failed reading active chat commands for user {UserId}",
                        (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                    activeChat = new ChatCommandConfiguration();
                }

                ApplyCoreSelectionToChatCommands(activeChat, selection);
                await _userRepository.SaveChatCommandsConfigAsync(safeUserId, activeChat);
                await _overlayNotifier.NotifyCustomAlertAsync(safeUserId, "chatCommandsUpdated", new { commands = activeChat.Commands });

                _logger.LogInformation(
                    "✅ Applied active core counter selection for user {UserId} game {GameId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ Failed applying active core counter selection for user {UserId} game {GameId}",
                    (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"),
                    (gameId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
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
