using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class LogsControllerTests
    {
        private readonly Mock<ILogger<LogsController>> _mockLogger;
        private readonly Mock<IWebHostEnvironment> _mockEnvironment;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly LogsController _controller;

        public LogsControllerTests()
        {
            _mockLogger = new Mock<ILogger<LogsController>>();
            _mockEnvironment = new Mock<IWebHostEnvironment>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockEnvironment.Setup(x => x.EnvironmentName).Returns("Development");

            _controller = new LogsController(_mockLogger.Object, _mockEnvironment.Object, _mockUserRepository.Object);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345"),
                new Claim("username", "testuser"),
                new Claim(ClaimTypes.Role, "admin")
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
        public void TestLogs_ShouldReturnOk()
        {
            var result = _controller.TestLogs();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public void GetQueries_ShouldReturnOk()
        {
            var result = _controller.GetQueries();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public void GetStats_ShouldReturnOk_WhenAdmin()
        {
            var result = _controller.GetStats();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }
    }
}
