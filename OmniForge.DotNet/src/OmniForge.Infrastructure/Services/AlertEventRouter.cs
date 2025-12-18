using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class AlertEventRouter : IAlertEventRouter
    {
        private readonly IAlertRepository _alertRepository;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<AlertEventRouter> _logger;

        public AlertEventRouter(
            IAlertRepository alertRepository,
            IOverlayNotifier overlayNotifier,
            ILogger<AlertEventRouter> logger)
        {
            _alertRepository = alertRepository;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public async Task RouteAsync(string userId, string eventKey, string defaultAlertType, object data)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var mappedAlertType = defaultAlertType;

            try
            {
                var mappings = await _alertRepository.GetEventMappingsAsync(userId);
                if (mappings.TryGetValue(eventKey, out var configuredType) && !string.IsNullOrWhiteSpace(configuredType))
                {
                    mappedAlertType = configuredType;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to load event mappings for {UserId}. Falling back to default alert type.", userId);
            }

            if (string.IsNullOrWhiteSpace(mappedAlertType) || string.Equals(mappedAlertType, "none", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("⏭️ Alert suppressed by mapping. user={UserId}, event={EventKey}", userId, eventKey);
                return;
            }

            await _overlayNotifier.NotifyCustomAlertAsync(userId, mappedAlertType, data);
        }
    }
}
