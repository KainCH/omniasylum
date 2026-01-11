using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using OmniForge.Infrastructure.Repositories;
using Xunit;

namespace OmniForge.Tests
{
    public class CounterRequestRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly CounterRequestRepository _repository;

        public CounterRequestRepositoryTests()
        {
            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockServiceClient.Setup(x => x.GetTableClient("counterrequests")).Returns(_mockTableClient.Object);
            _repository = new CounterRequestRepository(_mockServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task InitializeAsync_ShouldCreateTableIfNotExists()
        {
            await _repository.InitializeAsync();
            _mockTableClient.Verify(x => x.CreateIfNotExistsAsync(default), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_WhenNull_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.CreateAsync(null!));
        }

        [Fact]
        public async Task CreateAsync_WhenMissingRequestId_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _repository.CreateAsync(new CounterRequest { RequestId = "" }));
        }

        [Fact]
        public async Task CreateAsync_ShouldAddEntity()
        {
            var request = new CounterRequest
            {
                RequestId = "r1",
                Status = "pending",
                RequestedByUserId = "u1",
                Name = "Counter",
                Icon = "bi-star",
                Description = "desc",
                AdminNotes = "",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };

            await _repository.CreateAsync(request);

            _mockTableClient.Verify(x => x.AddEntityAsync(
                It.Is<CounterRequestTableEntity>(e => e.RowKey == "r1" && e.PartitionKey == "pending" && e.Name == "Counter"),
                default), Times.Once);
        }

        [Fact]
        public async Task GetAsync_WhenRequestIdBlank_ShouldReturnNull_WithoutQuery()
        {
            var result = await _repository.GetAsync(" ");
            Assert.Null(result);

            _mockTableClient.Verify(
                x => x.QueryAsync<CounterRequestTableEntity>(It.IsAny<string>(), null, null, default),
                Times.Never);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnFirstMatchingEntity_FromQuery()
        {
            var entity = new CounterRequestTableEntity
            {
                PartitionKey = "pending",
                RowKey = "r1",
                RequestedByUserId = "u1",
                Name = "Counter",
                Icon = "bi-star",
                Description = "desc",
                AdminNotes = "note",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };

            var page = Page<CounterRequestTableEntity>.FromValues(new[] { entity }, null, Mock.Of<Response>());
            var pageable = AsyncPageable<CounterRequestTableEntity>.FromPages(new[] { page });

            _mockTableClient
                .Setup(x => x.QueryAsync<CounterRequestTableEntity>(It.IsAny<string>(), null, null, default))
                .Returns(pageable);

            var result = await _repository.GetAsync("r1");

            Assert.NotNull(result);
            Assert.Equal("r1", result!.RequestId);
            Assert.Equal("pending", result.Status);
        }

        [Fact]
        public async Task ListAsync_WhenStatusProvided_ShouldUsePartitionKeyFilter_AndOrderByCreatedAtDesc()
        {
            string? capturedFilter = null;

            var e1 = new CounterRequestTableEntity { PartitionKey = "approved", RowKey = "r1", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
            var e2 = new CounterRequestTableEntity { PartitionKey = "approved", RowKey = "r2", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) };

            var page = Page<CounterRequestTableEntity>.FromValues(new[] { e1, e2 }, null, Mock.Of<Response>());
            var pageable = AsyncPageable<CounterRequestTableEntity>.FromPages(new[] { page });

            _mockTableClient
                .Setup(x => x.QueryAsync<CounterRequestTableEntity>(It.IsAny<string>(), null, null, default))
                .Callback<string, int?, IEnumerable<string>?, System.Threading.CancellationToken>((filter, _, _, _) => capturedFilter = filter)
                .Returns(pageable);

            var results = (await _repository.ListAsync("approved")).ToList();

            Assert.Equal("PartitionKey eq 'approved'", capturedFilter);
            Assert.Equal(2, results.Count);
            Assert.Equal("r2", results[0].RequestId);
            Assert.Equal("r1", results[1].RequestId);
        }

        [Fact]
        public async Task UpdateStatusAsync_WhenMissingArgs_ShouldThrow()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpdateStatusAsync("", "approved"));
            await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpdateStatusAsync("r1", ""));
        }

        [Fact]
        public async Task UpdateStatusAsync_WhenStatusChanges_ShouldDeleteOldEntity_AndUpsertNewEntity()
        {
            var existing = new CounterRequestTableEntity
            {
                PartitionKey = "pending",
                RowKey = "r1",
                RequestedByUserId = "u1",
                Name = "Counter",
                Icon = "bi-star",
                Description = "desc",
                AdminNotes = "",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };

            var page = Page<CounterRequestTableEntity>.FromValues(new[] { existing }, null, Mock.Of<Response>());
            var pageable = AsyncPageable<CounterRequestTableEntity>.FromPages(new[] { page });

            _mockTableClient
                .Setup(x => x.QueryAsync<CounterRequestTableEntity>(It.IsAny<string>(), null, null, default))
                .Returns(pageable);

            await _repository.UpdateStatusAsync("r1", "approved", adminNotes: "note");

            _mockTableClient.Verify(x => x.DeleteEntityAsync("pending", "r1", default, default), Times.Once);
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<CounterRequestTableEntity>(e => e.PartitionKey == "approved" && e.RowKey == "r1" && e.AdminNotes == "note"),
                TableUpdateMode.Replace,
                default), Times.Once);
        }

        [Fact]
        public async Task UpdateStatusAsync_WhenStatusSame_ShouldUpsertExistingEntity()
        {
            var existing = new CounterRequestTableEntity
            {
                PartitionKey = "pending",
                RowKey = "r1",
                RequestedByUserId = "u1",
                Name = "Counter",
                Icon = "bi-star",
                Description = "desc",
                AdminNotes = "",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };

            var page = Page<CounterRequestTableEntity>.FromValues(new[] { existing }, null, Mock.Of<Response>());
            var pageable = AsyncPageable<CounterRequestTableEntity>.FromPages(new[] { page });

            _mockTableClient
                .Setup(x => x.QueryAsync<CounterRequestTableEntity>(It.IsAny<string>(), null, null, default))
                .Returns(pageable);

            await _repository.UpdateStatusAsync("r1", "pending", adminNotes: "note");

            _mockTableClient.Verify(x => x.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), default, default), Times.Never);
            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<CounterRequestTableEntity>(e => e.PartitionKey == "pending" && e.RowKey == "r1" && e.AdminNotes == "note"),
                TableUpdateMode.Replace,
                default), Times.Once);
        }

        [Fact]
        public async Task UpdateStatusAsync_WhenEntityNotFound_ShouldThrowInvalidOperation()
        {
            var emptyPage = Page<CounterRequestTableEntity>.FromValues(Array.Empty<CounterRequestTableEntity>(), null, Mock.Of<Response>());
            var empty = AsyncPageable<CounterRequestTableEntity>.FromPages(new[] { emptyPage });

            _mockTableClient
                .Setup(x => x.QueryAsync<CounterRequestTableEntity>(It.IsAny<string>(), null, null, default))
                .Returns(empty);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.UpdateStatusAsync("r1", "approved"));
        }
    }
}
