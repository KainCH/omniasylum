using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class BotReactionService : IBotReactionService
    {
        private readonly ILogger<BotReactionService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ConcurrentDictionary<string, HashSet<string>> _greeted = new();

        public BotReactionService(
            ILogger<BotReactionService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task HandleStreamStartAsync(string broadcasterId)
        {
            var msg = await GetTemplateAsync(broadcasterId, s => s.StreamStartMessage);
            if (msg != null) await SendAsync(broadcasterId, msg);
        }

        public async Task HandleRaidReceivedAsync(string broadcasterId, string raiderName, int viewers)
        {
            var msg = await GetTemplateAsync(broadcasterId, s => s.RaidReceivedMessage);
            if (msg != null) await SendAsync(broadcasterId, FormatTokens(msg, ("{raider}", raiderName), ("{viewers}", viewers.ToString())));
        }

        public async Task HandleNewSubAsync(string broadcasterId, string username, string tier)
        {
            var msg = await GetTemplateAsync(broadcasterId, s => s.NewSubMessage);
            if (msg != null) await SendAsync(broadcasterId, FormatTokens(msg, ("{username}", username), ("{tier}", tier)));
        }

        public async Task HandleGiftSubAsync(string broadcasterId, string gifterName, int count)
        {
            var msg = await GetTemplateAsync(broadcasterId, s => s.GiftSubMessage);
            if (msg != null) await SendAsync(broadcasterId, FormatTokens(msg, ("{gifter}", gifterName), ("{count}", count.ToString())));
        }

        public async Task HandleResubAsync(string broadcasterId, string username, int months)
        {
            var msg = await GetTemplateAsync(broadcasterId, s => s.ResubMessage);
            if (msg != null) await SendAsync(broadcasterId, FormatTokens(msg, ("{username}", username), ("{months}", months.ToString())));
        }

        public async Task HandleFirstTimeChatAsync(string broadcasterId, string chatterId, string displayName)
        {
            var sessionSet = _greeted.GetOrAdd(broadcasterId, _ => new HashSet<string>());
            lock (sessionSet)
            {
                if (sessionSet.Contains(chatterId)) return;
                sessionSet.Add(chatterId);
            }

            var msg = await GetTemplateAsync(broadcasterId, s => s.FirstTimeChatMessage);
            if (msg != null) await SendAsync(broadcasterId, FormatTokens(msg, ("{username}", displayName)));
        }

        public async Task HandleClipCreatedAsync(string broadcasterId, string clipUrl)
        {
            var msg = await GetTemplateAsync(broadcasterId, s => s.ClipAnnouncementMessage);
            if (msg != null) await SendAsync(broadcasterId, FormatTokens(msg, ("{url}", clipUrl)));
        }

        public void ResetSession(string broadcasterId)
        {
            if (_greeted.TryGetValue(broadcasterId, out var set))
            {
                lock (set) { set.Clear(); }
            }
        }

        private async Task<string?> GetTemplateAsync(string broadcasterId, Func<BotSettings, string?> selector)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var user = await userRepository.GetUserAsync(broadcasterId);
                if (user?.BotSettings == null) return null;
                var template = selector(user.BotSettings);
                return string.IsNullOrWhiteSpace(template) ? null : template;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ BotReactionService failed to load template for {Broadcaster}", broadcasterId);
                return null;
            }
        }

        private async Task SendAsync(string broadcasterId, string message)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var twitchClientManager = scope.ServiceProvider.GetRequiredService<ITwitchClientManager>();
                await twitchClientManager.SendMessageAsync(broadcasterId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ BotReactionService failed to send message for {Broadcaster}", broadcasterId);
            }
        }

        private static string FormatTokens(string template, params (string token, string value)[] tokens)
        {
            foreach (var (token, value) in tokens)
                template = template.Replace(token, value, StringComparison.OrdinalIgnoreCase);
            return template;
        }
    }
}
