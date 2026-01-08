using System;
using System.Threading;
using System.Threading.Tasks;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class MemoryBotEligibilityCacheTests
    {
        [Fact]
        public async Task TryGetAsync_WhenEmpty_ReturnsNull()
        {
            var cache = new MemoryBotEligibilityCache();

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task SetAsync_ThenTryGetAsync_ReturnsCachedValue()
        {
            var cache = new MemoryBotEligibilityCache();
            var expected = new BotEligibilityResult(true, "botUserId", "ok");

            await cache.SetAsync("broadcaster", "bot", expected, TimeSpan.FromMinutes(5), CancellationToken.None);

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result!.UseBot);
            Assert.Equal("botUserId", result.BotUserId);
        }

        [Fact]
        public async Task TryGetAsync_WhenExpired_ReturnsNull()
        {
            var cache = new MemoryBotEligibilityCache();
            var expected = new BotEligibilityResult(true, "botUserId", "ok");

            await cache.SetAsync("broadcaster", "bot", expected, TimeSpan.FromMilliseconds(-1), CancellationToken.None);

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task CacheKey_IsCaseInsensitiveForBotLogin()
        {
            var cache = new MemoryBotEligibilityCache();
            var expected = new BotEligibilityResult(true, "botUserId", "ok");

            await cache.SetAsync("broadcaster", "BoT", expected, TimeSpan.FromMinutes(5), CancellationToken.None);

            var result = await cache.TryGetAsync("broadcaster", "bot", CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result!.UseBot);
        }
    }
}
