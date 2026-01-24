using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Utilities;

namespace OmniForge.Web.Services
{
    public class LoggingCircuitHandler : CircuitHandler
    {
        private readonly ILogger<LoggingCircuitHandler> _logger;

        public LoggingCircuitHandler(ILogger<LoggingCircuitHandler> logger)
        {
            _logger = logger;
        }

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            var circuitId = circuit?.Id ?? "unknown";
            _logger.LogInformation("Circuit opened: {CircuitId}", LogSanitizer.Sanitize(circuitId));
            return circuit == null ? Task.CompletedTask : base.OnCircuitOpenedAsync(circuit, cancellationToken);
        }

        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            var circuitId = circuit?.Id ?? "unknown";
            _logger.LogInformation("Connection up: {CircuitId}", LogSanitizer.Sanitize(circuitId));
            return circuit == null ? Task.CompletedTask : base.OnConnectionUpAsync(circuit, cancellationToken);
        }

        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            var circuitId = circuit?.Id ?? "unknown";
            _logger.LogInformation("Connection down: {CircuitId}", LogSanitizer.Sanitize(circuitId));
            return circuit == null ? Task.CompletedTask : base.OnConnectionDownAsync(circuit, cancellationToken);
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            var circuitId = circuit?.Id ?? "unknown";
            _logger.LogInformation("Circuit closed: {CircuitId}", LogSanitizer.Sanitize(circuitId));
            return circuit == null ? Task.CompletedTask : base.OnCircuitClosedAsync(circuit, cancellationToken);
        }
    }
}
