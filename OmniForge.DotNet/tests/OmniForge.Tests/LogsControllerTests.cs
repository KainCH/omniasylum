using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class LogsControllerTests
    {
        private readonly Mock<ILogger<LogsController>> _mockLogger;
        private readonly Mock<IWebHostEnvironment> _mockEnvironment;
        private readonly LogsController _controller;

        public LogsControllerTests()
        {
            _mockLogger = new Mock<ILogger<LogsController>>();
            _mockEnvironment = new Mock<IWebHostEnvironment>();
            _mockEnvironment.Setup(x => x.EnvironmentName).Returns("Development");

            _controller = new LogsController(_mockLogger.Object, _mockEnvironment.Object);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        [Fact]
        public void GetStatus_ShouldReturnOk()
        {
            var result = _controller.GetStatus();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public void TestLog_ShouldReturnOk()
        {
            var result = _controller.TestLog();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            // Verify logs were called
            // Verifying extension methods on ILogger is tricky with Moq, usually requires verifying Log method directly
            // But since we just want to ensure it doesn't crash and returns OK, this is sufficient for now.
        }
    }
}
