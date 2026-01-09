using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class DiscordBotPresenceHostedServiceTests
    {
        [Fact]
        public async Task StartAsync_WhenNoBotTokenConfigured_DoesNotCallEnsureOnline()
        {
            var logger = new Mock<ILogger<DiscordBotPresenceHostedService>>();
            var botClient = new Mock<IDiscordBotClient>();
            var settings = Options.Create(new DiscordBotSettings { BotToken = "" });

            var service = new DiscordBotPresenceHostedService(logger.Object, botClient.Object, settings);

            await service.StartAsync(CancellationToken.None);

            botClient.Verify(x => x.EnsureOnlineAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StartAsync_WhenBotTokenConfigured_CallsEnsureOnline()
        {
            var logger = new Mock<ILogger<DiscordBotPresenceHostedService>>();
            var botClient = new Mock<IDiscordBotClient>();
            var settings = Options.Create(new DiscordBotSettings { BotToken = "token" });

            var service = new DiscordBotPresenceHostedService(logger.Object, botClient.Object, settings);

            await service.StartAsync(CancellationToken.None);

            botClient.Verify(x => x.EnsureOnlineAsync("token", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task StartAsync_WhenEnsureOnlineThrows_DoesNotThrow()
        {
            var logger = new Mock<ILogger<DiscordBotPresenceHostedService>>();
            var botClient = new Mock<IDiscordBotClient>();
            botClient.Setup(x => x.EnsureOnlineAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("boom"));

            var settings = Options.Create(new DiscordBotSettings { BotToken = "token" });
            var service = new DiscordBotPresenceHostedService(logger.Object, botClient.Object, settings);

            await service.StartAsync(CancellationToken.None);
        }

        [Fact]
        public async Task StopAsync_DoesNotThrow()
        {
            var logger = new Mock<ILogger<DiscordBotPresenceHostedService>>();
            var botClient = new Mock<IDiscordBotClient>();
            var settings = Options.Create(new DiscordBotSettings { BotToken = "token" });

            var service = new DiscordBotPresenceHostedService(logger.Object, botClient.Object, settings);

            await service.StopAsync(CancellationToken.None);
        }
    }
}
