using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class BotModerationService : IBotModerationService
    {
        private readonly ILogger<BotModerationService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _linkViolations = new();

        private static readonly Regex UrlPattern = new Regex(
            @"https?://\S+|www\.\S+|[a-zA-Z0-9\-]+\.[a-zA-Z]{2,}(/\S*)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public BotModerationService(ILogger<BotModerationService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task<bool> CheckAndEnforceAsync(string broadcasterId, string chatterId, string chatterLogin,
            string messageId, string message, bool isMod, bool isBroadcaster)
        {
            if (isMod || isBroadcaster) return false;

            using var scope = _scopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var twitchApiService = scope.ServiceProvider.GetRequiredService<ITwitchApiService>();
            var twitchClientManager = scope.ServiceProvider.GetRequiredService<ITwitchClientManager>();

            var user = await userRepository.GetUserAsync(broadcasterId);
            var settings = user?.BotModeration;
            if (settings == null) return false;

            if (settings.AntiCapsEnabled && IsCapSpam(message, settings.CapsPercentThreshold, settings.CapsMinMessageLength))
            {
                _logger.LogInformation("Caps spam detected from {Login} in {Broadcaster}", chatterLogin, broadcasterId);
                try
                {
                    await twitchApiService.DeleteChatMessageAsync(broadcasterId, messageId);
                    await twitchApiService.BanUserAsync(broadcasterId, chatterId, "Caps spam bot detection");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to enforce caps spam for {Login} in {Broadcaster}", chatterLogin, broadcasterId);
                    return false;
                }
                return true;
            }

            if (settings.AntiSymbolSpamEnabled && IsSymbolSpam(message, settings.SymbolPercentThreshold))
            {
                _logger.LogInformation("Symbol spam detected from {Login} in {Broadcaster}", chatterLogin, broadcasterId);
                try
                {
                    await twitchApiService.DeleteChatMessageAsync(broadcasterId, messageId);
                    await twitchApiService.BanUserAsync(broadcasterId, chatterId, "Symbol spam bot detection");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to enforce symbol spam for {Login} in {Broadcaster}", chatterLogin, broadcasterId);
                    return false;
                }
                return true;
            }

            if (settings.LinkGuardEnabled && ContainsDisallowedUrl(message, settings.AllowedDomains))
            {
                _logger.LogInformation("Disallowed link from {Login} in {Broadcaster}", chatterLogin, broadcasterId);
                try
                {
                    await twitchApiService.DeleteChatMessageAsync(broadcasterId, messageId);

                    var violations = _linkViolations.GetOrAdd(broadcasterId, _ => new ConcurrentDictionary<string, int>());
                    var count = violations.AddOrUpdate(chatterId, 1, (_, v) => v + 1);

                    if (count >= 2)
                    {
                        await twitchApiService.BanUserAsync(broadcasterId, chatterId, "Repeated link posting");
                        _logger.LogInformation("Banned {Login} for repeated link posting in {Broadcaster}", chatterLogin, broadcasterId);
                    }
                    else
                    {
                        await twitchClientManager.SendMessageAsync(broadcasterId,
                            $"@{chatterLogin} Please do not post unsolicited links. Next offense will result in a ban.");
                        _logger.LogInformation("Warned {Login} for link posting in {Broadcaster}", chatterLogin, broadcasterId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to enforce link guard for {Login} in {Broadcaster}", chatterLogin, broadcasterId);
                    return false;
                }
                return true;
            }

            return false;
        }

        public void ResetSession(string broadcasterId)
        {
            if (_linkViolations.TryGetValue(broadcasterId, out var violations))
                violations.Clear();
        }

        private static bool IsCapSpam(string message, int threshold, int minLength)
        {
            if (message.Length < minLength) return false;
            int alphaCount = 0, upperCount = 0;
            foreach (var c in message)
            {
                if (char.IsLetter(c)) { alphaCount++; if (char.IsUpper(c)) upperCount++; }
            }
            if (alphaCount == 0) return false;
            return (upperCount * 100 / alphaCount) >= threshold;
        }

        private static bool IsSymbolSpam(string message, int threshold)
        {
            if (message.Length == 0) return false;
            int symbolCount = 0;
            foreach (var c in message)
            {
                if (!char.IsLetterOrDigit(c) && c != ' ') symbolCount++;
            }
            return (symbolCount * 100 / message.Length) >= threshold;
        }

        private static bool ContainsDisallowedUrl(string message, List<string> allowedDomains)
        {
            var matches = UrlPattern.Matches(message);
            if (matches.Count == 0) return false;

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var rawUrl = m.Value;
                if (!rawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    rawUrl = "https://" + rawUrl;

                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                    return true; // unparseable URL — treat as disallowed

                var host = uri.Host.ToLowerInvariant();
                if (host.StartsWith("www.")) host = host[4..];

                bool allowed = false;
                foreach (var domain in allowedDomains)
                {
                    var normalised = domain.ToLowerInvariant();
                    if (normalised.StartsWith("www.")) normalised = normalised[4..];

                    // Exact match or subdomain (host == domain or host ends with .domain)
                    if (host == normalised || host.EndsWith("." + normalised))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed) return true;
            }
            return false;
        }
    }
}
