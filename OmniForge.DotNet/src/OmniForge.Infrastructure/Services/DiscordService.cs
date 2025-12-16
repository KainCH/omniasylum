using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class DiscordService : IDiscordService
    {
        private static readonly Regex _templateTokenRegex = new Regex("{{\\s*(?<key>[a-zA-Z0-9_]+)\\s*}}", RegexOptions.Compiled);

        private readonly HttpClient _httpClient;
        private readonly ILogger<DiscordService> _logger;
        private readonly DiscordBotSettings _discordBotSettings;
        private readonly IDiscordBotClient _discordBotClient;

        public DiscordService(HttpClient httpClient, ILogger<DiscordService> logger, IOptions<DiscordBotSettings> discordBotSettings, IDiscordBotClient discordBotClient)
        {
            _httpClient = httpClient;
            _logger = logger;
            _discordBotSettings = discordBotSettings.Value;
            _discordBotClient = discordBotClient;
        }

        public async Task SendTestNotificationAsync(User user)
        {
            if (string.IsNullOrEmpty(user.DiscordChannelId) && string.IsNullOrEmpty(user.DiscordWebhookUrl))
            {
                _logger.LogWarning("Attempted to send test notification but no Discord destination is configured for user {Username}", LogSanitizer.Sanitize(user.Username));
                return;
            }

            _logger.LogInformation("Sending Discord test notification for {Username}", LogSanitizer.Sanitize(user.Username));

            var options = new DiscordEmbedOptions { Color = 0x00FF00 };
            var payload = CreateDiscordPayload(
                "Discord Integration Test",
                $"This is a test notification for **{user.DisplayName}**.\n\nIf you see this, your Discord destination is configured correctly! 🎉",
                user,
                options
            );

            var effectiveChannelId = string.IsNullOrWhiteSpace(user.DiscordChannelId) ? null : user.DiscordChannelId;
            await SendDiscordMessageAsync(user, effectiveChannelId, payload, options);
        }

        public async Task SendNotificationAsync(User user, string eventType, object data)
        {
            var template = GetMessageTemplate(user, eventType);
            var effectiveChannelId = GetEffectiveChannelId(user, template);

            _logger.LogInformation("📤 Discord notification request: User={Username}, EventType={EventType}, ChannelId={ChannelId}, LegacyWebhookConfigured={LegacyWebhook}",
                LogSanitizer.Sanitize(user.Username),
                LogSanitizer.Sanitize(eventType),
                string.IsNullOrEmpty(effectiveChannelId) ? "EMPTY" : LogSanitizer.Sanitize(effectiveChannelId),
                !string.IsNullOrEmpty(user.DiscordWebhookUrl));

            if (string.IsNullOrEmpty(effectiveChannelId) && string.IsNullOrEmpty(user.DiscordWebhookUrl))
            {
                _logger.LogWarning("⚠️ No Discord destination configured for user {Username}", LogSanitizer.Sanitize(user.Username));
                return;
            }

            // Check if notification is enabled
            if (!IsNotificationEnabled(user, eventType))
            {
                _logger.LogInformation("Discord notification disabled for {EventType} by user {Username}", LogSanitizer.Sanitize(eventType), LogSanitizer.Sanitize(user.Username));
                return;
            }

            string title;
            string? description = null;
            int color;
            var options = new DiscordEmbedOptions();

            // Use dynamic to access properties of the anonymous object or dictionary passed as data
            dynamic eventData = data;

            // Build the token map once per event and apply templates (if set)
            var (streamStartMentions, streamStartAllowedMentions) = eventType == "stream_start" ? BuildStreamStartMentions(user) : (null, AllowedMentions.None);
            var tokens = BuildTemplateTokens(user, eventType, eventData, streamStartMentions);

            switch (eventType)
            {
                case "death_milestone":
                    title = $"💀 Death Milestone: {GetProperty(eventData, "count")}";
                    description = $"**{user.DisplayName}** has reached {GetProperty(eventData, "count")} deaths!\n\n📊 **Progress:** {GetProperty(eventData, "previousMilestone") ?? 0} → {GetProperty(eventData, "count")}";
                    color = 0xFF4444; // Red
                    break;

                case "swear_milestone":
                    title = $"🤬 Swear Milestone: {GetProperty(eventData, "count")}";
                    description = $"**{user.DisplayName}** has reached {GetProperty(eventData, "count")} swears!\n\n📊 **Progress:** {GetProperty(eventData, "previousMilestone") ?? 0} → {GetProperty(eventData, "count")}";
                    color = 0xFF8800; // Orange
                    break;

                case "scream_milestone":
                    title = $"😱 Scream Milestone: {GetProperty(eventData, "count")}";
                    description = $"**{user.DisplayName}** has reached {GetProperty(eventData, "count")} screams!\n\n📊 **Progress:** {GetProperty(eventData, "previousMilestone") ?? 0} → {GetProperty(eventData, "count")}";
                    color = 0xFFFF00; // Yellow
                    break;

                case "stream_start":
                    title = $"🔴 {user.DisplayName} is now live on Twitch!";
                    description = null;
                    color = 0x00FF00; // Green

                    options.Content = streamStartMentions;
                    options.AllowedMentions = streamStartAllowedMentions;

                    var streamTitle = !string.IsNullOrEmpty((string?)GetProperty(eventData, "title")) ? (string?)GetProperty(eventData, "title") : "Stream Title Not Set";
                    var gameName = !string.IsNullOrEmpty((string?)GetProperty(eventData, "game")) ? (string?)GetProperty(eventData, "game") : "Unknown Category";

                    options.Fields = new List<DiscordField>
                    {
                        new DiscordField { Name = "📺 Title", Value = streamTitle!, Inline = false },
                        new DiscordField { Name = "🎮 Streaming", Value = gameName!, Inline = true }
                    };

                    var thumbnailUrl = (string?)GetProperty(eventData, "thumbnailUrl");
                    if (!string.IsNullOrEmpty(thumbnailUrl))
                    {
                        options.ImageUrl = thumbnailUrl;
                    }
                    else
                    {
                        options.ImageUrl = $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{user.Username.ToLower()}-640x360.jpg?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    }

                    options.Url = $"https://twitch.tv/{user.Username}";
                    options.Buttons = new List<DiscordButton>
                    {
                        new DiscordButton
                        {
                            Label = "🎮 **READY TO WATCH? CLICK HERE!**",
                            Url = $"https://twitch.tv/{user.Username}",
                            Style = 5
                        }
                    };
                    break;

                case "stream_end":
                    title = "🔴 Stream Ended";
                    description = $"**{user.DisplayName}** has ended the stream.\n\n⏱️ **Duration:** {GetProperty(eventData, "duration") ?? "Unknown"}\n💙 **Thanks for watching!**";
                    color = 0xFF4444; // Red
                    break;

                default:
                    title = (string?)GetProperty(eventData, "title") ?? "📢 OmniForge Notification";
                    description = (string?)GetProperty(eventData, "description") ?? $"Event: {eventType}";
                    color = (int?)GetProperty(eventData, "color") ?? 0x5865F2; // Discord blurple
                    break;
            }

            // Apply per-event template overrides (if provided). Defaults remain when templates are blank.
            ApplyTemplateOverrides(template, ref title, ref description, options, tokens);

            options.Color = color;
            var payload = CreateDiscordPayload(title, description, user, options);
            await SendDiscordMessageAsync(user, effectiveChannelId, payload, options);
        }

        private static DiscordMessageTemplate? GetMessageTemplate(User user, string eventType)
        {
            var templates = user.DiscordSettings?.MessageTemplates;
            if (templates == null) return null;

            return templates.TryGetValue(eventType, out var t) ? t : null;
        }

        private static string? GetEffectiveChannelId(User user, DiscordMessageTemplate? template)
        {
            var overrideId = template?.ChannelIdOverride;
            if (!string.IsNullOrWhiteSpace(overrideId)) return overrideId.Trim();
            if (!string.IsNullOrWhiteSpace(user.DiscordChannelId)) return user.DiscordChannelId.Trim();
            return null;
        }

        private static Dictionary<string, string> BuildTemplateTokens(User user, string eventType, object eventData, string? streamStartMentions)
        {
            var twitchUrl = !string.IsNullOrWhiteSpace(user.Username) ? $"https://twitch.tv/{user.Username}" : string.Empty;

            string GetValue(string key)
            {
                if (eventData == null) return string.Empty;

                if (eventData is IDictionary<string, object> dict)
                {
                    return dict.TryGetValue(key, out var v) ? (v?.ToString() ?? string.Empty) : string.Empty;
                }

                if (eventData is JsonElement jsonElement)
                {
                    return jsonElement.TryGetProperty(key, out var v) ? (v.ToString() ?? string.Empty) : string.Empty;
                }

                var prop = eventData.GetType().GetProperty(key);
                return prop?.GetValue(eventData)?.ToString() ?? string.Empty;
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["eventType"] = eventType,
                ["displayName"] = user.DisplayName ?? string.Empty,
                ["username"] = user.Username ?? string.Empty,
                ["twitchUrl"] = twitchUrl,
                ["streamStartMentions"] = streamStartMentions ?? string.Empty,

                // Common event payload fields
                ["count"] = GetValue("count"),
                ["previousMilestone"] = GetValue("previousMilestone"),
                ["title"] = GetValue("title"),
                ["game"] = GetValue("game"),
                ["thumbnailUrl"] = GetValue("thumbnailUrl"),
                ["duration"] = GetValue("duration")
            };
        }

        private string RenderTemplate(string template, IReadOnlyDictionary<string, string> tokens)
        {
            if (string.IsNullOrWhiteSpace(template)) return string.Empty;

            return _templateTokenRegex.Replace(template, m =>
            {
                var key = m.Groups["key"].Value;
                return tokens.TryGetValue(key, out var value) ? value : m.Value;
            });
        }

        private void ApplyTemplateOverrides(DiscordMessageTemplate? template, ref string title, ref string? description, DiscordEmbedOptions options, IReadOnlyDictionary<string, string> tokens)
        {
            if (template == null) return;

            if (!string.IsNullOrWhiteSpace(template.TitleTemplate))
            {
                var rendered = RenderTemplate(template.TitleTemplate, tokens).Trim();
                if (!string.IsNullOrEmpty(rendered)) title = TrimToMax(rendered, 256);
            }

            if (!string.IsNullOrWhiteSpace(template.DescriptionTemplate))
            {
                var rendered = RenderTemplate(template.DescriptionTemplate, tokens).Trim();
                description = string.IsNullOrEmpty(rendered) ? null : TrimToMax(rendered, 4096);
            }

            if (!string.IsNullOrWhiteSpace(template.ContentTemplate))
            {
                var rendered = RenderTemplate(template.ContentTemplate, tokens).Trim();
                options.Content = string.IsNullOrEmpty(rendered) ? null : TrimToMax(rendered, 2000);
            }
        }

        private static string TrimToMax(string value, int maxLength)
        {
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength);
        }

        private static (string? content, AllowedMentions allowedMentions) BuildStreamStartMentions(User user)
        {
            var settings = user.DiscordSettings;
            if (settings == null) return (null, AllowedMentions.None);

            var parts = new List<string>();
            var allowEveryone = settings.MentionEveryoneOnStreamStart;

            ulong? roleId = null;
            if (!string.IsNullOrWhiteSpace(settings.MentionRoleIdOnStreamStart) && ulong.TryParse(settings.MentionRoleIdOnStreamStart, out var parsedRoleId))
            {
                roleId = parsedRoleId;
            }

            if (!allowEveryone && roleId == null)
            {
                return (null, AllowedMentions.None);
            }

            if (allowEveryone)
            {
                parts.Add("@everyone");
            }

            if (roleId != null)
            {
                parts.Add($"<@&{roleId.Value}>");
            }

            var content = string.Join(" ", parts);

            // Discord.Net: explicitly allow only the mentions we intend.
            var mentions = new AllowedMentions
            {
                AllowedTypes = AllowedMentionTypes.None
            };

            if (allowEveryone)
            {
                mentions.AllowedTypes |= AllowedMentionTypes.Everyone;
            }

            if (roleId != null)
            {
                mentions.AllowedTypes |= AllowedMentionTypes.Roles;
                mentions.RoleIds = new List<ulong> { roleId.Value };
            }

            return (content, mentions);
        }

        public async Task<bool> ValidateDiscordChannelAsync(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return false;
            if (string.IsNullOrWhiteSpace(_discordBotSettings.BotToken)) return false;

            try
            {
                return await _discordBotClient.ValidateChannelAsync(channelId, _discordBotSettings.BotToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating Discord channel ID");
                return false;
            }
        }

        private object? GetProperty(object obj, string propertyName)
        {
            if (obj == null) return null;

            // Handle Dictionary
            if (obj is IDictionary<string, object> dict)
            {
                return dict.ContainsKey(propertyName) ? dict[propertyName] : null;
            }

            // Handle JsonElement (if passed from deserialized JSON)
            if (obj is JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty(propertyName, out var value))
                {
                    return value.ToString();
                }
                return null;
            }

            // Handle anonymous types or regular objects via reflection
            var prop = obj.GetType().GetProperty(propertyName);
            return prop?.GetValue(obj);
        }

        private bool IsNotificationEnabled(User user, string eventType)
        {
            if (user.DiscordSettings?.EnabledNotifications == null) return true; // Default to true if settings missing

            return eventType switch
            {
                "death_milestone" => user.DiscordSettings.EnabledNotifications.DeathMilestone,
                "swear_milestone" => user.DiscordSettings.EnabledNotifications.SwearMilestone,
                "scream_milestone" => user.DiscordSettings.EnabledNotifications.ScreamMilestone,
                "stream_start" => user.DiscordSettings.EnabledNotifications.StreamStart,
                "stream_end" => user.DiscordSettings.EnabledNotifications.StreamEnd,
                "follower_goal" => user.DiscordSettings.EnabledNotifications.FollowerGoal,
                "subscriber_milestone" => user.DiscordSettings.EnabledNotifications.SubscriberMilestone,
                "channel_point_redemption" => user.DiscordSettings.EnabledNotifications.ChannelPointRedemption,
                _ => false
            };
        }

        public async Task<bool> ValidateWebhookAsync(string webhookUrl)
        {
            if (string.IsNullOrEmpty(webhookUrl)) return false;

            try
            {
                var response = await _httpClient.GetAsync(webhookUrl);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating Discord webhook URL: {Url}", LogSanitizer.Sanitize(webhookUrl));
                return false;
            }
        }

        private async Task SendDiscordMessageAsync(User user, string? effectiveChannelId, object embedPayload, DiscordEmbedOptions options)
        {
            // Preferred path: Bot token + channelId
            if (!string.IsNullOrWhiteSpace(effectiveChannelId))
            {
                if (string.IsNullOrWhiteSpace(_discordBotSettings.BotToken))
                {
                    _logger.LogError("❌ Discord bot token is not configured (DiscordBot:BotToken). Cannot send Discord messages.");
                    throw new InvalidOperationException("Discord bot token is not configured");
                }

                var embed = CreateDiscordNetEmbed(title: null, description: null, user: user, options: options, prebuiltWebhookPayload: embedPayload);
                var components = CreateDiscordNetComponents(options);
                await _discordBotClient.SendMessageAsync(
                    effectiveChannelId,
                    _discordBotSettings.BotToken,
                    options.Content,
                    embed,
                    components,
                    options.AllowedMentions ?? AllowedMentions.None);
                return;
            }

            // Legacy fallback: webhook
            if (!string.IsNullOrWhiteSpace(user.DiscordWebhookUrl))
            {
                _logger.LogWarning("⚠️ Sending Discord notification via legacy webhook for {Username}. Migrate to channelId for better security.", LogSanitizer.Sanitize(user.Username));
                var webhookUrl = user.DiscordWebhookUrl;
                if (options.Buttons != null && options.Buttons.Count > 0)
                {
                    webhookUrl += "?with_components=true";
                }
                await SendWebhookAsync(webhookUrl, embedPayload);
            }
        }

        private async Task SendWebhookAsync(string url, object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogInformation("Sending Discord webhook to {Url}", LogSanitizer.Sanitize(url));

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Discord webhook failed with {StatusCode}: {ErrorText}", response.StatusCode, LogSanitizer.Sanitize(errorText));

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new HttpRequestException($"Discord webhook URL is invalid or has been deleted. Please update your settings with a new webhook URL. Details: {errorText}");
                    }

                    throw new HttpRequestException($"Discord webhook failed: {response.StatusCode} {errorText}");
                }

                _logger.LogInformation("Discord notification sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Discord notification");
                throw;
            }
        }

        private object CreateDiscordPayload(string title, string? description, User user, DiscordEmbedOptions options)
        {
            var embed = CreateDiscordEmbed(title, description, user, options);
            return CreateDiscordWebhookPayload(embed, user, options);
        }

        private Embed CreateDiscordNetEmbed(string? title, string? description, User user, DiscordEmbedOptions options, object? prebuiltWebhookPayload = null)
        {
            var embedBuilder = new EmbedBuilder();

            if (prebuiltWebhookPayload != null)
            {
                var embedsProp = prebuiltWebhookPayload.GetType().GetProperty("embeds");
                var embedsObj = embedsProp?.GetValue(prebuiltWebhookPayload) as Array;
                var firstEmbed = embedsObj != null && embedsObj.Length > 0 ? embedsObj.GetValue(0) : null;

                if (firstEmbed != null)
                {
                    var extractedTitle = firstEmbed.GetType().GetProperty("title")?.GetValue(firstEmbed)?.ToString();
                    var extractedDescription = firstEmbed.GetType().GetProperty("description")?.GetValue(firstEmbed)?.ToString();
                    var extractedUrl = firstEmbed.GetType().GetProperty("url")?.GetValue(firstEmbed)?.ToString();
                    var extractedColor = firstEmbed.GetType().GetProperty("color")?.GetValue(firstEmbed);
                    var extractedTimestamp = firstEmbed.GetType().GetProperty("timestamp")?.GetValue(firstEmbed)?.ToString();

                    embedBuilder.Title = extractedTitle ?? title ?? string.Empty;
                    embedBuilder.Description = extractedDescription ?? description;
                    embedBuilder.Url = extractedUrl;
                    if (extractedColor is int c)
                    {
                        embedBuilder.Color = new Color((uint)c);
                    }
                    else
                    {
                        embedBuilder.Color = new Color((uint)options.Color);
                    }

                    if (DateTimeOffset.TryParse(extractedTimestamp, out var dto))
                    {
                        embedBuilder.Timestamp = dto;
                    }
                }
                else
                {
                    embedBuilder.Title = title ?? string.Empty;
                    embedBuilder.Description = description;
                    embedBuilder.Color = new Color((uint)options.Color);
                }
            }
            else
            {
                embedBuilder.Title = title ?? string.Empty;
                embedBuilder.Description = description;
                embedBuilder.Color = new Color((uint)options.Color);
            }

            if (!string.IsNullOrWhiteSpace(options.Url)) embedBuilder.Url = options.Url;
            if (!string.IsNullOrWhiteSpace(user.ProfileImageUrl)) embedBuilder.ThumbnailUrl = user.ProfileImageUrl;
            embedBuilder.Footer = new EmbedFooterBuilder { Text = "OmniForge Stream Tools" };
            if (embedBuilder.Timestamp == null) embedBuilder.Timestamp = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(options.ImageUrl))
            {
                embedBuilder.ImageUrl = options.ImageUrl;
            }

            if (options.Fields != null)
            {
                foreach (var field in options.Fields)
                {
                    embedBuilder.AddField(field.Name, field.Value, field.Inline);
                }
            }

            return embedBuilder.Build();
        }

        private MessageComponent? CreateDiscordNetComponents(DiscordEmbedOptions options)
        {
            if (options.Buttons == null || options.Buttons.Count == 0) return null;

            var builder = new ComponentBuilder();
            for (var i = 0; i < options.Buttons.Count; i++)
            {
                var button = options.Buttons[i];
                var row = i / 5;
                builder.WithButton(label: button.Label, url: button.Url, style: ButtonStyle.Link, row: row);
            }

            return builder.Build();
        }

        private object CreateDiscordEmbed(string title, string? description, User user, DiscordEmbedOptions options)
        {
            return new
            {
                title,
                description,
                color = options.Color,
                url = options.Url,
                thumbnail = new { url = user.ProfileImageUrl },
                footer = new { text = "OmniForge Stream Tools" },
                timestamp = DateTime.UtcNow.ToString("o"),
                fields = options.Fields,
                image = !string.IsNullOrEmpty(options.ImageUrl) ? new { url = options.ImageUrl } : null
            };
        }

        private object? CreateDiscordComponents(DiscordEmbedOptions options)
        {
            if (options.Buttons == null || options.Buttons.Count == 0) return null;

            return new[]
            {
                new
                {
                    type = 1,
                    components = options.Buttons
                }
            };
        }

        private object CreateDiscordWebhookPayload(object embed, User user, DiscordEmbedOptions options)
        {
            return new
            {
                username = "OmniForge",
                avatar_url = options.BotAvatar ?? user.ProfileImageUrl,
                content = options.Content,
                allowed_mentions = CreateWebhookAllowedMentions(options),
                embeds = new[] { embed },
                components = CreateDiscordComponents(options)
            };
        }

        private static object? CreateWebhookAllowedMentions(DiscordEmbedOptions options)
        {
            var allowed = options.AllowedMentions;
            if (allowed == null) return null;

            // If no mention types are enabled, omit allowed_mentions entirely.
            if (allowed.AllowedTypes == AllowedMentionTypes.None) return null;

            var parse = new List<string>();
            if ((allowed.AllowedTypes & AllowedMentionTypes.Everyone) == AllowedMentionTypes.Everyone)
            {
                parse.Add("everyone");
            }

            return new
            {
                parse,
                roles = allowed.RoleIds
            };
        }

        private class DiscordEmbedOptions
        {
            public string? BotAvatar { get; set; }
            public int Color { get; set; } = 0x5865F2;
            public string? Url { get; set; }
            public string? ImageUrl { get; set; }
            public List<DiscordField>? Fields { get; set; }
            public List<DiscordButton>? Buttons { get; set; }

            public string? Content { get; set; }
            public AllowedMentions? AllowedMentions { get; set; }
        }

        private class DiscordField
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public bool Inline { get; set; }
        }

        private class DiscordButton
        {
            public int Type { get; } = 2; // Button
            public int Style { get; set; } = 5; // Link
            public string Label { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }
    }
}
