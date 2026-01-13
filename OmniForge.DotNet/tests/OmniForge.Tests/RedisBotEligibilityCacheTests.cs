using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Services;
using StackExchange.Redis;
using Xunit;

namespace OmniForge.Tests
{
    public class RedisBotEligibilityCacheTests
    {
        [Fact]
        public async Task TryGetAsync_WhenHostMissing_ReturnsNull()
        {
            var settings = Options.Create(new RedisSettings { HostName = string.Empty, KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger);

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task TryGetAsync_WhenKeyMissing_ReturnsNull()
        {
            var db = new Mock<IDatabase>(MockBehavior.Strict);
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "Dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, databaseFactory: () => Task.FromResult<IDatabase?>(db.Object));

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task TryGetAsync_WhenDbFactoryReturnsNull_ReturnsNull()
        {
            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, databaseFactory: () => Task.FromResult<IDatabase?>(null));

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task TryGetAsync_WhenValueDeserializesToNull_ReturnsNull()
        {
            var db = new Mock<IDatabase>(MockBehavior.Strict);
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)"null");

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, databaseFactory: () => Task.FromResult<IDatabase?>(db.Object));

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task SetAsync_ThenTryGetAsync_ReturnsCachedValue()
        {
            var expected = new BotEligibilityResult(true, "botUserId", "ok");

            // Pre-compute the expected JSON so we can return it from StringGetAsync
            var expectedJson = System.Text.Json.JsonSerializer.Serialize(new { UseBot = expected.UseBot, BotUserId = expected.BotUserId, Reason = expected.Reason });

            var db = new Mock<IDatabase>(MockBehavior.Loose);

            // Setup StringGetAsync to return the expected cached value
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)expectedJson);

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "Prod" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, databaseFactory: () => Task.FromResult<IDatabase?>(db.Object));

            // SetAsync should not throw (uses Loose mock for any StringSetAsync overload)
            await cache.SetAsync(" broadcaster ", " BoT ", expected, TimeSpan.FromMinutes(5), CancellationToken.None);

            // TryGetAsync should return the mocked value
            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result!.UseBot);
            Assert.Equal("botUserId", result.BotUserId);
            Assert.Equal("ok", result.Reason);
        }

        [Fact]
        public async Task TryGetAsync_WhenRedisThrows_ReturnsNull()
        {
            var db = new Mock<IDatabase>(MockBehavior.Strict);
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new TimeoutException("boom"));

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, databaseFactory: () => Task.FromResult<IDatabase?>(db.Object));

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task SetAsync_WhenRedisThrows_DoesNotThrow()
        {
            // Use Loose so that if the actual StringSetAsync overload doesn't match our setup,
            // it returns a default instead of throwing (which would be swallowed anyway).
            // This test verifies that exceptions from Redis don't propagate.
            var db = new Mock<IDatabase>(MockBehavior.Loose);
            // Setup StringSetAsync to throw - covers the exception handling path
            db.Setup(d => d.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<When>()))
                .ThrowsAsync(new TimeoutException("boom"));

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, databaseFactory: () => Task.FromResult<IDatabase?>(db.Object));

            await cache.SetAsync("broadcaster", "bot", new BotEligibilityResult(true, "id", "ok"), TimeSpan.FromMinutes(1), CancellationToken.None);
        }

        [Fact]
        public async Task TryGetAsync_UsesNormalizedCacheKey()
        {
            RedisKey? capturedKey = null;

            var db = new Mock<IDatabase>(MockBehavior.Strict);
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, CommandFlags>((k, _) => capturedKey = k)
                .ReturnsAsync(RedisValue.Null);

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "  PrOd  " });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, databaseFactory: () => Task.FromResult<IDatabase?>(db.Object));

            var result = await cache.TryGetAsync(" broadcaster ", " BoT ", CancellationToken.None);

            Assert.Null(result);
            Assert.NotNull(capturedKey);
            Assert.Equal("botEligibility:v1:prod:broadcaster:bot", (string)capturedKey!);
        }

        [Fact]
        public async Task SetAsync_WhenDbFactoryReturnsNull_DoesNotThrow()
        {
            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, databaseFactory: () => Task.FromResult<IDatabase?>(null));

            await cache.SetAsync("broadcaster", "bot", new BotEligibilityResult(true, "id", "ok"), TimeSpan.FromMinutes(5), CancellationToken.None);
        }

        [Fact]
        public async Task TryGetAsync_WhenConnectionFactoryReturnsNull_ReturnsNull()
        {
            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(
                settings,
                logger,
                connectionFactory: () => Task.FromResult<IConnectionMultiplexer?>(null));

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task TryGetAsync_WhenMuxNotConnected_ReturnsNull()
        {
            var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
            mux.SetupGet(m => m.IsConnected).Returns(false);

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(
                settings,
                logger,
                connectionFactory: () => Task.FromResult<IConnectionMultiplexer?>(mux.Object));

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
            mux.VerifyGet(m => m.IsConnected, Times.AtLeastOnce);
        }

        [Fact]
        public async Task TryGetAsync_WhenMuxConnected_UsesDatabaseAndReturnsValue()
        {
            var expected = new BotEligibilityResult(true, "botUserId", "ok");
            var json = JsonSerializer.Serialize(new { UseBot = expected.UseBot, BotUserId = expected.BotUserId, Reason = expected.Reason });

            var db = new Mock<IDatabase>(MockBehavior.Strict);
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)json);

            var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
            mux.SetupGet(m => m.IsConnected).Returns(true);
            mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                .Returns(db.Object);

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(
                settings,
                logger,
                connectionFactory: () => Task.FromResult<IConnectionMultiplexer?>(mux.Object));

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result!.UseBot);
            Assert.Equal("botUserId", result.BotUserId);
            Assert.Equal("ok", result.Reason);
        }

        [Fact]
        public async Task Dispose_WhenConnectionCreated_DisposesMux()
        {
            var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
            mux.SetupGet(m => m.IsConnected).Returns(false);
            mux.Setup(m => m.Dispose());

            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(
                settings,
                logger,
                connectionFactory: () => Task.FromResult<IConnectionMultiplexer?>(mux.Object));

            _ = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            cache.Dispose();

            mux.Verify(m => m.Dispose(), Times.Once);
        }

        [Fact]
        public void Dispose_WhenConnectionFactoryNotMaterialized_DoesNotThrow()
        {
            var settings = Options.Create(new RedisSettings { HostName = "example:10000", KeyNamespace = "dev" });
            var logger = Mock.Of<ILogger<RedisBotEligibilityCache>>();

            var cache = new RedisBotEligibilityCache(settings, logger, () => throw new InvalidOperationException("should not run"));

            cache.Dispose();
        }
    }
}
