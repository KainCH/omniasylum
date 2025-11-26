using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class TemplateControllerTests
    {
        private readonly Mock<ITemplateRepository> _mockTemplateRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly TemplateController _controller;

        public TemplateControllerTests()
        {
            _mockTemplateRepository = new Mock<ITemplateRepository>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _controller = new TemplateController(
                _mockTemplateRepository.Object,
                _mockUserRepository.Object,
                _mockOverlayNotifier.Object);

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
        public async Task GetAvailableTemplates_ShouldReturnOk()
        {
            var templates = new Dictionary<string, Template>
            {
                { "test", new Template { Name = "Test" } }
            };
            _mockTemplateRepository.Setup(x => x.GetAvailableTemplatesAsync()).ReturnsAsync(templates);

            var result = await _controller.GetAvailableTemplates();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetCurrentTemplate_ShouldReturnOk_WhenUserExists()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { TemplateStyle = "test" } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var templates = new Dictionary<string, Template>
            {
                { "test", new Template { Name = "Test" } }
            };
            _mockTemplateRepository.Setup(x => x.GetAvailableTemplatesAsync()).ReturnsAsync(templates);

            var result = await _controller.GetCurrentTemplate();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var template = Assert.IsType<Template>(okResult.Value);
            Assert.Equal("test", template.TemplateStyle);
        }

        [Fact]
        public async Task GetCurrentTemplate_ShouldReturnCustom_WhenStyleIsCustom()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { TemplateStyle = "custom" } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var customTemplate = new Template { Name = "Custom", Type = "custom" };
            _mockTemplateRepository.Setup(x => x.GetUserCustomTemplateAsync("12345")).ReturnsAsync(customTemplate);

            var result = await _controller.GetCurrentTemplate();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var template = Assert.IsType<Template>(okResult.Value);
            Assert.Equal("custom", template.TemplateStyle);
        }

        [Fact]
        public async Task SelectTemplate_ShouldReturnBadRequest_WhenInvalidStyle()
        {
            var request = new SelectTemplateRequest { TemplateStyle = "invalid" };
            _mockTemplateRepository.Setup(x => x.GetAvailableTemplatesAsync()).ReturnsAsync(new Dictionary<string, Template>());

            var result = await _controller.SelectTemplate(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SelectTemplate_ShouldReturnOk_WhenValidStyle()
        {
            var request = new SelectTemplateRequest { TemplateStyle = "test" };
            var templates = new Dictionary<string, Template>
            {
                { "test", new Template { Name = "Test" } }
            };
            _mockTemplateRepository.Setup(x => x.GetAvailableTemplatesAsync()).ReturnsAsync(templates);

            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags() };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.SelectTemplate(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Features.TemplateStyle == "test")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyTemplateChangedAsync("12345", "test", It.IsAny<Template>()), Times.Once);
        }

        [Fact]
        public async Task SaveCustomTemplate_ShouldReturnOk_WhenValid()
        {
            var request = new SaveCustomTemplateRequest
            {
                Name = "My Custom Template",
                Config = new TemplateConfig
                {
                    Colors = new TemplateColors(),
                    Fonts = new TemplateFonts(),
                    Animations = new TemplateAnimations()
                },
                MakeActive = true
            };

            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags() };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.SaveCustomTemplate(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockTemplateRepository.Verify(x => x.SaveUserCustomTemplateAsync("12345", It.Is<Template>(t => t.Name == "My Custom Template")), Times.Once);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Features.TemplateStyle == "custom")), Times.Once);
        }
    }
}
