using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class AlertEventRouterTests
    {
        [Fact]
        public async Task RouteAsync_WhenMappingOverridesType_ShouldNotifyOverlayWithConfiguredType()
        {
            var alertRepository = new Mock<IAlertRepository>();
            alertRepository
                .Setup(x => x.GetEventMappingsAsync("user1"))
                .ReturnsAsync(new Dictionary<string, string> { { "event.key", "configured_type" } });

            var overlayNotifier = new Mock<IOverlayNotifier>();
            var logger = new Mock<ILogger<AlertEventRouter>>();

            var router = new AlertEventRouter(alertRepository.Object, overlayNotifier.Object, logger.Object);

            await router.RouteAsync("user1", "event.key", "default_type", new { value = 1 });

            overlayNotifier.Verify(x => x.NotifyCustomAlertAsync("user1", "configured_type", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task RouteAsync_WhenMappingSuppressesAlert_ShouldNotNotifyOverlay()
        {
            var alertRepository = new Mock<IAlertRepository>();
            alertRepository
                .Setup(x => x.GetEventMappingsAsync("user1"))
                .ReturnsAsync(new Dictionary<string, string> { { "event.key", "none" } });

            var overlayNotifier = new Mock<IOverlayNotifier>();
            var logger = new Mock<ILogger<AlertEventRouter>>();

            var router = new AlertEventRouter(alertRepository.Object, overlayNotifier.Object, logger.Object);

            await router.RouteAsync("user1", "event.key", "default_type", new { value = 1 });

            overlayNotifier.Verify(x => x.NotifyCustomAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        }
    }
}
