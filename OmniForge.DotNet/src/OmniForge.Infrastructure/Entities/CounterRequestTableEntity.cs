using System;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class CounterRequestTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "pending"; // status
        public string RowKey { get; set; } = string.Empty; // RequestId
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string RequestedByUserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AdminNotes { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public CounterRequest ToRequest()
        {
            return new CounterRequest
            {
                RequestId = RowKey,
                RequestedByUserId = RequestedByUserId,
                Name = Name,
                Icon = Icon,
                Description = Description,
                Status = PartitionKey,
                AdminNotes = AdminNotes,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };
        }

        public static CounterRequestTableEntity FromRequest(CounterRequest request)
        {
            return new CounterRequestTableEntity
            {
                PartitionKey = request.Status,
                RowKey = request.RequestId,
                RequestedByUserId = request.RequestedByUserId,
                Name = request.Name,
                Icon = request.Icon,
                Description = request.Description,
                AdminNotes = request.AdminNotes,
                CreatedAt = request.CreatedAt,
                UpdatedAt = request.UpdatedAt
            };
        }
    }
}
