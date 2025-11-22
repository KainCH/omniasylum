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

        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string VisualCue { get; set; } = string.Empty;
        public string Sound { get; set; } = string.Empty;
        public string SoundDescription { get; set; } = string.Empty;
        public string TextPrompt { get; set; } = string.Empty;
        public int Duration { get; set; }
        public string BackgroundColor { get; set; } = string.Empty;
        public string TextColor { get; set; } = string.Empty;
        public string BorderColor { get; set; } = string.Empty;
        public string Effects { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsDefault { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public Alert ToAlert()
        {
            return new Alert
            {
                Id = RowKey,
                UserId = PartitionKey,
                Type = Type,
                Name = Name,
                VisualCue = VisualCue,
                Sound = Sound,
                SoundDescription = SoundDescription,
                TextPrompt = TextPrompt,
                Duration = Duration,
                BackgroundColor = BackgroundColor,
                TextColor = TextColor,
                BorderColor = BorderColor,
                Effects = Effects,
                IsEnabled = IsEnabled,
                IsDefault = IsDefault,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };
        }

        public static AlertTableEntity FromAlert(Alert alert)
        {
            return new AlertTableEntity
            {
                PartitionKey = alert.UserId,
                RowKey = alert.Id,
                Type = alert.Type,
                Name = alert.Name,
                VisualCue = alert.VisualCue,
                Sound = alert.Sound,
                SoundDescription = alert.SoundDescription,
                TextPrompt = alert.TextPrompt,
                Duration = alert.Duration,
                BackgroundColor = alert.BackgroundColor,
                TextColor = alert.TextColor,
                BorderColor = alert.BorderColor,
                Effects = alert.Effects,
                IsEnabled = alert.IsEnabled,
                IsDefault = alert.IsDefault,
                CreatedAt = alert.CreatedAt,
                UpdatedAt = alert.UpdatedAt
            };
        }
    }
}
