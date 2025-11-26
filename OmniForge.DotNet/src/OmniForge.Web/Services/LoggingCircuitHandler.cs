using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
            _logger.LogInformation("Circuit opened: {CircuitId}", circuit.Id);
            return base.OnCircuitOpenedAsync(circuit, cancellationToken);
        }

        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Connection up: {CircuitId}", circuit.Id);
            return base.OnConnectionUpAsync(circuit, cancellationToken);
        }

        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Connection down: {CircuitId}", circuit.Id);
            return base.OnConnectionDownAsync(circuit, cancellationToken);
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Circuit closed: {CircuitId}", circuit.Id);
            return base.OnCircuitClosedAsync(circuit, cancellationToken);
        }
    }
}
