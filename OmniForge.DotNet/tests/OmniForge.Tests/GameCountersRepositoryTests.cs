using System;
using System.Collections.Generic;
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
    public class GameCountersRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient = new();
        private readonly Mock<TableClient> _mockTableClient = new();
        private readonly GameCountersRepository _repository;

        public GameCountersRepositoryTests()
        {
            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockServiceClient.Setup(x => x.GetTableClient("gamecounters")).Returns(_mockTableClient.Object);
            _repository = new GameCountersRepository(_mockServiceClient.Object, tableConfig);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnNull_WhenNotFound()
        {
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>("u1", "g1", null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetAsync("u1", "g1");
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_ShouldMapCoreCounters_WithCaseFallback_AndCustomCounters()
        {
            var entity = new TableEntity("u1", "g1")
            {
                ["deaths"] = 3,
                ["Swears"] = 4,
                ["screams"] = 5,
                ["Bits"] = 6,
                ["lastUpdated"] = DateTimeOffset.UtcNow,
                ["customCounters"] = "{\"Test\":10,\"test2\":20}"
            };

            var response = Response.FromValue(entity, Mock.Of<Response>());
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>("u1", "g1", null, default))
                .ReturnsAsync(response);

            var result = await _repository.GetAsync("u1", "g1");

            Assert.NotNull(result);
            Assert.Equal(3, result!.Deaths);
            Assert.Equal(4, result.Swears);
            Assert.Equal(5, result.Screams);
            Assert.Equal(6, result.Bits);
            Assert.NotNull(result.CustomCounters);
            Assert.Equal(10, result.CustomCounters!["test"]);
            Assert.Equal(20, result.CustomCounters!["TEST2"]);
        }

        [Fact]
        public async Task GetAsync_WhenCustomCountersInvalidJson_ShouldReturnEmptyCustomCounters()
        {
            var entity = new TableEntity("u1", "g1")
            {
                ["Deaths"] = 1,
                ["customCounters"] = "{bad json]"
            };

            var response = Response.FromValue(entity, Mock.Of<Response>());
            _mockTableClient
                .Setup(x => x.GetEntityAsync<TableEntity>("u1", "g1", null, default))
                .ReturnsAsync(response);

            var result = await _repository.GetAsync("u1", "g1");

            Assert.NotNull(result);
            Assert.NotNull(result!.CustomCounters);
            Assert.Empty(result.CustomCounters!);
        }

        [Fact]
        public async Task SaveAsync_ShouldUpsertEntityWithSerializedCustomCounters()
        {
            var counters = new Counter
            {
                TwitchUserId = "u1",
                Deaths = 1,
                Swears = 2,
                Screams = 3,
                Bits = 4,
                LastUpdated = DateTimeOffset.UtcNow,
                CustomCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["abc"] = 7
                }
            };

            await _repository.SaveAsync("u1", "g1", counters);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e =>
                    e.PartitionKey == "u1"
                    && e.RowKey == "g1"
                    && (int)e["Deaths"] == 1
                    && (string)e["customCounters"]! == "{\"abc\":7}"),
                TableUpdateMode.Replace,
                default), Times.Once);
        }
    }
}
