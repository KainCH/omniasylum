using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Configuration;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Entities;
using OmniForge.Infrastructure.Repositories;
using Xunit;

namespace OmniForge.Tests
{
    public class UserRepositoryTests
    {
        private readonly Mock<TableServiceClient> _mockServiceClient;
        private readonly Mock<TableClient> _mockTableClient;
        private readonly Mock<ILogger<UserRepository>> _mockLogger;
        private readonly UserRepository _repository;

        public UserRepositoryTests()
        {
            _mockServiceClient = new Mock<TableServiceClient>();
            _mockTableClient = new Mock<TableClient>();
            _mockLogger = new Mock<ILogger<UserRepository>>();

            var tableConfig = Options.Create(new AzureTableConfiguration());
            _mockServiceClient.Setup(x => x.GetTableClient("users")).Returns(_mockTableClient.Object);

            _repository = new UserRepository(_mockServiceClient.Object, tableConfig, _mockLogger.Object);
        }

        [Fact]
        public async Task GetUserAsync_ShouldReturnUser_WhenExists()
        {
            var userId = "123";
            // Use TableEntity to match the repository implementation
            var entity = new TableEntity("user", userId)
            {
                { "twitchUserId", userId },
                { "username", "testuser" },
                { "displayName", "Test User" },
                { "role", "streamer" },
                { "features", "{}" },
                { "isActive", true }
            };

            var response = Response.FromValue(entity, Mock.Of<Response>());

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>("user", userId, null, default))
                .ReturnsAsync(response);

            var result = await _repository.GetUserAsync(userId);

            Assert.NotNull(result);
            Assert.Equal(userId, result.TwitchUserId);
            Assert.Equal("testuser", result.Username);
        }

        [Fact]
        public async Task GetUserAsync_ShouldReturnNull_WhenNotFound()
        {
            var userId = "123";

            _mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>("user", userId, null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetUserAsync(userId);

            Assert.Null(result);
        }

        [Fact]
        public async Task SaveUserAsync_ShouldUpsertEntity()
        {
            var user = new User
            {
                TwitchUserId = "123",
                Username = "testuser",
                Role = "streamer"
            };

            await _repository.SaveUserAsync(user);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<UserTableEntity>(e => e.RowKey == "123" && e.username == "testuser"),
                TableUpdateMode.Replace,
                default), Times.Once);
        }

        [Fact]
        public async Task DeleteUserAsync_ShouldDeleteEntity()
        {
            var userId = "123";

            await _repository.DeleteUserAsync(userId);

            _mockTableClient.Verify(x => x.DeleteEntityAsync("user", userId, default, default), Times.Once);
        }

        [Fact]
        public async Task GetAllUsersAsync_ShouldReturnAllUsers()
        {
            // Use TableEntity to match the repository implementation
            var entities = new List<TableEntity>
            {
                new TableEntity("user", "1") { { "twitchUserId", "1" }, { "username", "user1" }, { "role", "streamer" }, { "features", "{}" }, { "isActive", true } },
                new TableEntity("user", "2") { { "twitchUserId", "2" }, { "username", "user2" }, { "role", "streamer" }, { "features", "{}" }, { "isActive", true } }
            };

            var page = Page<TableEntity>.FromValues(entities, null, Mock.Of<Response>());
            var pages = AsyncPageable<TableEntity>.FromPages(new[] { page });

            _mockTableClient.Setup(x => x.QueryAsync<TableEntity>(It.IsAny<string>(), null, null, default))
                .Returns(pages);

            var result = await _repository.GetAllUsersAsync();

            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task GetChatCommandsConfigAsync_ShouldReturnConfig_WhenExists()
        {
            var userId = "123";
            var entity = new ChatCommandConfigTableEntity
            {
                PartitionKey = userId,
                RowKey = "chatCommands",
                commandsConfig = "{\"Commands\":{\"!test\":{\"Response\":\"Response\"}}}"
            };

            var response = Response.FromValue(entity, Mock.Of<Response>());

            _mockTableClient.Setup(x => x.GetEntityAsync<ChatCommandConfigTableEntity>(userId, "chatCommands", null, default))
                .ReturnsAsync(response);

            var result = await _repository.GetChatCommandsConfigAsync(userId);

            Assert.NotNull(result);
            Assert.Single(result.Commands);
            Assert.True(result.Commands.ContainsKey("!test"));
            Assert.Equal("Response", result.Commands["!test"].Response);
        }

        [Fact]
        public async Task GetChatCommandsConfigAsync_ShouldReturnEmpty_WhenNotFound()
        {
            var userId = "123";

            _mockTableClient.Setup(x => x.GetEntityAsync<ChatCommandConfigTableEntity>(userId, "chatCommands", null, default))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var result = await _repository.GetChatCommandsConfigAsync(userId);

            Assert.NotNull(result);
            Assert.Empty(result.Commands);
        }

        [Fact]
        public async Task SaveChatCommandsConfigAsync_ShouldUpsertEntity()
        {
            var userId = "123";
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!test", new ChatCommandDefinition { Response = "Response" } }
                }
            };

            await _repository.SaveChatCommandsConfigAsync(userId, config);

            _mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<ChatCommandConfigTableEntity>(e => e.PartitionKey == userId && e.RowKey == "chatCommands"),
                TableUpdateMode.Replace,
                default), Times.Once);
        }
    }
}
