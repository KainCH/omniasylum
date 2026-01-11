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
using OmniForge.Infrastructure.Repositories;
using Xunit;

namespace OmniForge.Tests
{
    public class GameLibraryRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly GameLibraryRepository _repository;

        public GameLibraryRepositoryTests()
        {
            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockServiceClient.Setup(x => x.GetTableClient("games")).Returns(_mockTableClient.Object);
            _repository = new GameLibraryRepository(_mockServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task UpsertAsync_WhenGameIdMissing_ShouldNoOp()
        {
            await _repository.UpsertAsync(new GameLibraryItem { GameId = "" });

            _mockTableClient.Verify(
                x => x.UpsertEntityAsync(It.IsAny<TableEntity>(), It.IsAny<TableUpdateMode>(), default),
                Times.Never);
        }

        [Fact]
        public async Task UpsertAsync_WhenEnabledCclsNull_ShouldNotWriteEnabledCclsProperty()
        {
            var item = new GameLibraryItem
            {
                GameId = "1",
                GameName = "Test",
                EnabledContentClassificationLabels = null,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                LastSeenAt = DateTimeOffset.UtcNow
            };

            await _repository.UpsertAsync(item);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e => e.PartitionKey == "global" && e.RowKey == "1" && !e.ContainsKey("enabledCcls")),
                TableUpdateMode.Merge,
                default), Times.Once);
        }

        [Fact]
        public async Task UpsertAsync_WhenEnabledCclsProvided_ShouldSerializeEnabledCcls()
        {
            var item = new GameLibraryItem
            {
                GameId = "1",
                GameName = "Test",
                EnabledContentClassificationLabels = new List<string> { "Gambling", "ProfanityVulgarity" },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                LastSeenAt = DateTimeOffset.UtcNow
            };

            TableEntity? capturedEntity = null;
            _mockTableClient
                .Setup(x => x.UpsertEntityAsync(It.IsAny<TableEntity>(), TableUpdateMode.Merge, default))
                .Callback<TableEntity, TableUpdateMode, System.Threading.CancellationToken>((e, _, _) => capturedEntity = e)
                .ReturnsAsync(Mock.Of<Response>());

            await _repository.UpsertAsync(item);

            Assert.NotNull(capturedEntity);
            Assert.Equal("global", capturedEntity!.PartitionKey);
            Assert.Equal("1", capturedEntity.RowKey);
            Assert.True(capturedEntity.TryGetValue("enabledCcls", out var enabledCclsValue));

            var enabledCclsJson = enabledCclsValue as string;
            Assert.False(string.IsNullOrWhiteSpace(enabledCclsJson));
            Assert.Contains("Gambling", enabledCclsJson);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnNull_WhenNotFound()
        {
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>("global", "1", null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetAsync("user", "1");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_WhenEnabledCclsInvalidJson_ShouldReturnEmptyList()
        {
            var entity = new TableEntity("global", "1")
            {
                ["gameName"] = "Test",
                ["boxArtUrl"] = "url",
                ["createdAt"] = DateTimeOffset.UtcNow.AddDays(-2),
                ["lastSeenAt"] = DateTimeOffset.UtcNow.AddDays(-1),
                ["enabledCcls"] = "{bad json]"
            };

            var response = Response.FromValue(entity, Mock.Of<Response>());
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>("global", "1", null, default))
                .ReturnsAsync(response);

            var result = await _repository.GetAsync("user", "1");

            Assert.NotNull(result);
            Assert.Equal("1", result!.GameId);
            Assert.NotNull(result.EnabledContentClassificationLabels);
            Assert.Empty(result.EnabledContentClassificationLabels!);
        }

        [Fact]
        public async Task ListAsync_ShouldStopAtTake_AndOrderByLastSeenAtDesc()
        {
            var older = new TableEntity("global", "1")
            {
                ["gameName"] = "Older",
                ["boxArtUrl"] = "url",
                ["createdAt"] = DateTimeOffset.UtcNow.AddDays(-3),
                ["lastSeenAt"] = DateTimeOffset.UtcNow.AddDays(-2)
            };
            var newer = new TableEntity("global", "2")
            {
                ["gameName"] = "Newer",
                ["boxArtUrl"] = "url",
                ["createdAt"] = DateTimeOffset.UtcNow.AddDays(-2),
                ["lastSeenAt"] = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var newest = new TableEntity("global", "3")
            {
                ["gameName"] = "Newest",
                ["boxArtUrl"] = "url",
                ["createdAt"] = DateTimeOffset.UtcNow.AddDays(-1),
                ["lastSeenAt"] = DateTimeOffset.UtcNow
            };

            var page = Page<TableEntity>.FromValues(new[] { older, newer, newest }, null, Mock.Of<Response>());
            var pageable = AsyncPageable<TableEntity>.FromPages(new[] { page });

            _mockTableClient
                .Setup(x => x.QueryAsync<TableEntity>(It.IsAny<string>(), null, null, default))
                .Returns(pageable);

            var results = await _repository.ListAsync("user", take: 2);

            Assert.Equal(2, results.Count);
            Assert.Equal("2", results[0].GameId);
            Assert.Equal("1", results[1].GameId);
        }
    }
}
