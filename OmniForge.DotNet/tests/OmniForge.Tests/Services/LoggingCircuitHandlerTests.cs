using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class LoggingCircuitHandlerTests
    {
        [Fact]
        public async Task CircuitLifecycleMethods_ShouldLogInformation()
        {
            var logger = new Mock<ILogger<LoggingCircuitHandler>>();
            var handler = new LoggingCircuitHandler(logger.Object);

            // Circuit is hard to construct in unit tests (internal constructors);
            // handler should be resilient and still log a useful message.
            await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);
            await handler.OnConnectionUpAsync(null!, CancellationToken.None);
            await handler.OnConnectionDownAsync(null!, CancellationToken.None);
            await handler.OnCircuitClosedAsync(null!, CancellationToken.None);

            VerifyLogged(logger, "Circuit opened");
            VerifyLogged(logger, "Connection up");
            VerifyLogged(logger, "Connection down");
            VerifyLogged(logger, "Circuit closed");
        }

        private static void VerifyLogged(Mock<ILogger<LoggingCircuitHandler>> logger, string contains)
        {
            logger.Verify(l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString() != null && v.ToString()!.Contains(contains, StringComparison.Ordinal)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }
    }
}
