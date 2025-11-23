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
        public int duration { get; set; }
        public string backgroundColor { get; set; } = string.Empty;
        public string textColor { get; set; } = string.Empty;
        public string borderColor { get; set; } = string.Empty;
        public string effects { get; set; } = string.Empty;
        public bool isEnabled { get; set; }
        public bool isDefault { get; set; }
        public object? createdAt { get; set; }
        public object? updatedAt { get; set; }

        private DateTimeOffset ParseDateTimeOffset(object? value)
        {
            if (value is DateTimeOffset dto) return dto;
            if (value is DateTime dt) return new DateTimeOffset(dt);
            if (value is string s && DateTimeOffset.TryParse(s, out var result)) return result;
            return default;
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
                Duration = duration,
                BackgroundColor = backgroundColor,
                TextColor = textColor,
                BorderColor = borderColor,
                Effects = effects,
                IsEnabled = isEnabled,
                IsDefault = isDefault,
                CreatedAt = ParseDateTimeOffset(createdAt),
                UpdatedAt = ParseDateTimeOffset(updatedAt)
            };
        }

        public static AlertTableEntity FromAlert(Alert alert)
        {
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
                createdAt = alert.CreatedAt,
                updatedAt = alert.UpdatedAt
            };
        }
    }
}
