using System;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class CoreCounterLibrarySeederTests
    {
        [Fact]
        public async Task SeedAsync_ShouldUpsertFourStableCoreCounters()
        {
            var repo = new Mock<ICounterLibraryRepository>();
            var seeder = new CoreCounterLibrarySeeder(repo.Object);

            await seeder.SeedAsync();

            repo.Verify(r => r.UpsertAsync(It.IsAny<CounterLibraryItem>()), Times.Exactly(4));

            var captured = repo.Invocations
                .Where(i => i.Method.Name == nameof(ICounterLibraryRepository.UpsertAsync))
                .Select(i => (CounterLibraryItem)i.Arguments[0]!)
                .ToList();

            Assert.Contains(captured, c => c.CounterId == "deaths" && c.Name == "Deaths" && c.Icon == "bi-skull");
            Assert.Contains(captured, c => c.CounterId == "swears" && c.Name == "Swears" && c.Icon == "bi-chat-dots");
            Assert.Contains(captured, c => c.CounterId == "screams" && c.Name == "Screams" && c.Icon == "bi-volume-up");
            Assert.Contains(captured, c => c.CounterId == "bits" && c.Name == "Bits" && c.Icon == "bi-gem");

            Assert.All(captured, c =>
            {
                Assert.True(c.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
                Assert.True(c.LastUpdated > DateTimeOffset.UtcNow.AddMinutes(-1));
            });
        }
    }
}
