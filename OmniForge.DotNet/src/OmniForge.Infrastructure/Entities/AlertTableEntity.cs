using System;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class AlertTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // AlertId
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string type { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string visualCue { get; set; } = string.Empty;
        public string sound { get; set; } = string.Empty;
        public string soundDescription { get; set; } = string.Empty;
        public string textPrompt { get; set; } = string.Empty;
        public object? duration { get; set; } // Can be int or string from legacy data
        public string backgroundColor { get; set; } = string.Empty;
        public string textColor { get; set; } = string.Empty;
        public string borderColor { get; set; } = string.Empty;
        public string effects { get; set; } = string.Empty;
        public object? isEnabled { get; set; } // Can be bool or string from legacy data
        public object? isDefault { get; set; } // Can be bool or string from legacy data
        public object? createdAt { get; set; }
        public object? updatedAt { get; set; }

        private static DateTimeOffset ParseDateTimeOffset(object? value)
        {
            if (value is DateTimeOffset dto) return dto;
            if (value is DateTime dt) return new DateTimeOffset(dt);
            if (value is string s && DateTimeOffset.TryParse(s, out var result)) return result;
            // Return current time as fallback - Azure Table Storage doesn't accept dates before 1601
            return DateTimeOffset.UtcNow;
        }

        private static int ParseInt(object? value, int defaultValue = 5000)
        {
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is string s && int.TryParse(s, out var parsed)) return parsed;
            return defaultValue;
        }

        private static bool ParseBool(object? value, bool defaultValue = false)
        {
            if (value is bool b) return b;
            if (value is string s && bool.TryParse(s, out var parsed)) return parsed;
            return defaultValue;
        }

        public Alert ToAlert()
        {
            return new Alert
            {
                Id = RowKey,
                UserId = PartitionKey,
                Type = type,
                Name = name,
                VisualCue = visualCue,
                Sound = sound,
                SoundDescription = soundDescription,
                TextPrompt = textPrompt,
                Duration = ParseInt(duration, 5000),
                BackgroundColor = backgroundColor,
                TextColor = textColor,
                BorderColor = borderColor,
                Effects = effects,
                IsEnabled = ParseBool(isEnabled, true),
                IsDefault = ParseBool(isDefault, false),
                CreatedAt = ParseDateTimeOffset(createdAt),
                UpdatedAt = ParseDateTimeOffset(updatedAt)
            };
        }

        public static AlertTableEntity FromAlert(Alert alert)
        {
            // Azure Table Storage requires dates after 1601-01-01
            // Use current time if the date is default/unset
            var minValidDate = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var now = DateTimeOffset.UtcNow;

            var createdAtValue = alert.CreatedAt < minValidDate ? now : alert.CreatedAt;
            var updatedAtValue = alert.UpdatedAt < minValidDate ? now : alert.UpdatedAt;

            return new AlertTableEntity
            {
                PartitionKey = alert.UserId,
                RowKey = alert.Id,
                type = alert.Type,
                name = alert.Name,
                visualCue = alert.VisualCue,
                sound = alert.Sound,
                soundDescription = alert.SoundDescription,
                textPrompt = alert.TextPrompt,
                duration = alert.Duration,
                backgroundColor = alert.BackgroundColor,
                textColor = alert.TextColor,
                borderColor = alert.BorderColor,
                effects = alert.Effects,
                isEnabled = alert.IsEnabled,
                isDefault = alert.IsDefault,
                createdAt = createdAtValue,
                updatedAt = updatedAtValue
            };
        }
    }
}
