using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniForge.Core.Exceptions;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class AutomodControllerTests
    {
        private readonly Mock<ITwitchApiService> _mockTwitchApiService;
        private readonly AutomodController _controller;

        public AutomodControllerTests()
        {
            _mockTwitchApiService = new Mock<ITwitchApiService>();
            _controller = new AutomodController(_mockTwitchApiService.Object, NullLogger<AutomodController>.Instance);

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
        public async Task GetSettings_ShouldReturnOk()
        {
            var dto = new AutomodSettingsDto { Aggression = 2 };
            _mockTwitchApiService.Setup(x => x.GetAutomodSettingsAsync("12345")).ReturnsAsync(dto);

            var result = await _controller.GetSettings();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(dto, ok.Value);
        }

        [Fact]
        public async Task UpdateSettings_ShouldReturnOk()
        {
            var dto = new AutomodSettingsDto { Aggression = 3 };
            _mockTwitchApiService.Setup(x => x.UpdateAutomodSettingsAsync("12345", dto)).ReturnsAsync(dto);

            var result = await _controller.UpdateSettings(dto);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(dto, ok.Value);
        }

        [Fact]
        public async Task GetSettings_WhenNoUserIdClaim_ShouldReturnUnauthorized()
        {
            var controller = new AutomodController(_mockTwitchApiService.Object, NullLogger<AutomodController>.Instance);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            };

            var result = await controller.GetSettings();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetSettings_WhenReauthRequired_ShouldReturnUnauthorizedPayload()
        {
            _mockTwitchApiService
                .Setup(x => x.GetAutomodSettingsAsync("12345"))
                .ThrowsAsync(new ReauthRequiredException("expired"));

            var result = await _controller.GetSettings();

            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.NotNull(unauthorized.Value);
        }

        [Fact]
        public async Task UpdateSettings_WhenInvalidOperation_ShouldReturn403()
        {
            var dto = new AutomodSettingsDto { Aggression = 3 };
            _mockTwitchApiService
                .Setup(x => x.UpdateAutomodSettingsAsync("12345", dto))
                .ThrowsAsync(new InvalidOperationException("blocked"));

            var result = await _controller.UpdateSettings(dto);

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, obj.StatusCode);
        }

        [Fact]
        public async Task GetSettings_WhenUnexpectedException_ShouldReturn500()
        {
            _mockTwitchApiService
                .Setup(x => x.GetAutomodSettingsAsync("12345"))
                .ThrowsAsync(new Exception("boom"));

            var result = await _controller.GetSettings();

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, obj.StatusCode);
        }
    }
}
