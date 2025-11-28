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

        [Fact]
        public async Task GetCurrentTemplate_ShouldReturnUnauthorized_WhenNoUserId()
        {
            // Arrange - controller with no userId claim
            var controllerWithoutUser = new TemplateController(
                _mockTemplateRepository.Object,
                _mockUserRepository.Object,
                _mockOverlayNotifier.Object);

            controllerWithoutUser.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            };

            var result = await controllerWithoutUser.GetCurrentTemplate();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetCurrentTemplate_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.GetCurrentTemplate();

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetCurrentTemplate_ShouldReturnFallback_WhenStyleNotFound()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { TemplateStyle = "nonexistent" } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var templates = new Dictionary<string, Template>
            {
                { "asylum_themed", new Template { Name = "Asylum" } }
            };
            _mockTemplateRepository.Setup(x => x.GetAvailableTemplatesAsync()).ReturnsAsync(templates);

            var result = await _controller.GetCurrentTemplate();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var template = Assert.IsType<Template>(okResult.Value);
            Assert.Equal("asylum_themed", template.TemplateStyle);
        }

        [Fact]
        public async Task GetCurrentTemplate_ShouldReturnDefaultTemplate_WhenNoStyleSet()
        {
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags { TemplateStyle = null } };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var templates = new Dictionary<string, Template>
            {
                { "asylum_themed", new Template { Name = "Asylum" } }
            };
            _mockTemplateRepository.Setup(x => x.GetAvailableTemplatesAsync()).ReturnsAsync(templates);

            var result = await _controller.GetCurrentTemplate();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var template = Assert.IsType<Template>(okResult.Value);
            Assert.Equal("asylum_themed", template.TemplateStyle);
        }

        [Fact]
        public async Task SelectTemplate_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controllerWithoutUser = new TemplateController(
                _mockTemplateRepository.Object,
                _mockUserRepository.Object,
                _mockOverlayNotifier.Object);

            controllerWithoutUser.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            };

            var request = new SelectTemplateRequest { TemplateStyle = "test" };
            var result = await controllerWithoutUser.SelectTemplate(request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task SelectTemplate_ShouldReturnBadRequest_WhenEmptyStyle()
        {
            var request = new SelectTemplateRequest { TemplateStyle = "" };

            var result = await _controller.SelectTemplate(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SelectTemplate_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            var request = new SelectTemplateRequest { TemplateStyle = "test" };
            var templates = new Dictionary<string, Template>
            {
                { "test", new Template { Name = "Test" } }
            };
            _mockTemplateRepository.Setup(x => x.GetAvailableTemplatesAsync()).ReturnsAsync(templates);
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);

            var result = await _controller.SelectTemplate(request);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task SelectTemplate_ShouldAllowCustomStyle()
        {
            var request = new SelectTemplateRequest { TemplateStyle = "custom" };
            var user = new User { TwitchUserId = "12345", Features = new FeatureFlags() };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockTemplateRepository.Setup(x => x.GetAvailableTemplatesAsync()).ReturnsAsync(new Dictionary<string, Template>());

            var customTemplate = new Template { Name = "Custom" };
            _mockTemplateRepository.Setup(x => x.GetUserCustomTemplateAsync("12345")).ReturnsAsync(customTemplate);

            var result = await _controller.SelectTemplate(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Features.TemplateStyle == "custom")), Times.Once);
        }

        [Fact]
        public async Task GetCustomTemplate_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controllerWithoutUser = new TemplateController(
                _mockTemplateRepository.Object,
                _mockUserRepository.Object,
                _mockOverlayNotifier.Object);

            controllerWithoutUser.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            };

            var result = await controllerWithoutUser.GetCustomTemplate();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetCustomTemplate_ShouldReturnOk()
        {
            var customTemplate = new Template { Name = "Custom" };
            _mockTemplateRepository.Setup(x => x.GetUserCustomTemplateAsync("12345")).ReturnsAsync(customTemplate);

            var result = await _controller.GetCustomTemplate();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(customTemplate, okResult.Value);
        }

        [Fact]
        public async Task SaveCustomTemplate_ShouldReturnUnauthorized_WhenNoUserId()
        {
            var controllerWithoutUser = new TemplateController(
                _mockTemplateRepository.Object,
                _mockUserRepository.Object,
                _mockOverlayNotifier.Object);

            controllerWithoutUser.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            };

            var request = new SaveCustomTemplateRequest { Name = "Test" };
            var result = await controllerWithoutUser.SaveCustomTemplate(request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task SaveCustomTemplate_ShouldReturnBadRequest_WhenNameMissing()
        {
            var request = new SaveCustomTemplateRequest
            {
                Name = "",
                Config = new TemplateConfig()
            };

            var result = await _controller.SaveCustomTemplate(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SaveCustomTemplate_ShouldReturnBadRequest_WhenConfigMissing()
        {
            var request = new SaveCustomTemplateRequest
            {
                Name = "Test",
                Config = null!
            };

            var result = await _controller.SaveCustomTemplate(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SaveCustomTemplate_ShouldReturnBadRequest_WhenConfigInvalid()
        {
            var request = new SaveCustomTemplateRequest
            {
                Name = "Test",
                Config = new TemplateConfig { Colors = null! }
            };

            var result = await _controller.SaveCustomTemplate(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SaveCustomTemplate_ShouldNotUpdateTemplateStyle_WhenMakeActiveIsFalse()
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
                MakeActive = false
            };

            var result = await _controller.SaveCustomTemplate(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockTemplateRepository.Verify(x => x.SaveUserCustomTemplateAsync("12345", It.IsAny<Template>()), Times.Once);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
