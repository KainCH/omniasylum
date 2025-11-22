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
    public class ChatCommandControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly ChatCommandController _controller;

        public ChatCommandControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _controller = new ChatCommandController(_mockUserRepository.Object);

            // Setup User Context
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
        public async Task GetChatCommands_ShouldReturnOk_WhenAuthenticated()
        {
            var config = new ChatCommandConfiguration();
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(config);

            var result = await _controller.GetChatCommands();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(config, okResult.Value);
        }

        [Fact]
        public async Task SaveChatCommands_ShouldReturnOk_WhenValid()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!test", new ChatCommandDefinition { Response = "Test" } }
                }
            };

            var result = await _controller.SaveChatCommands(config);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345", config), Times.Once);
        }

        [Fact]
        public async Task AddChatCommand_ShouldReturnOk_WhenNewCommand()
        {
            var config = new ChatCommandConfiguration();
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(config);

            var request = new AddCommandRequest
            {
                Command = "!new",
                Config = new ChatCommandDefinition { Response = "New" }
            };

            var result = await _controller.AddChatCommand(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345", It.Is<ChatCommandConfiguration>(c => c.Commands.ContainsKey("!new"))), Times.Once);
        }

        [Fact]
        public async Task AddChatCommand_ShouldReturnBadRequest_WhenCommandExists()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!exists", new ChatCommandDefinition { Response = "Exists" } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(config);

            var request = new AddCommandRequest
            {
                Command = "!exists",
                Config = new ChatCommandDefinition { Response = "New" }
            };

            var result = await _controller.AddChatCommand(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void GetDefaults_ShouldReturnOk()
        {
            var result = _controller.GetDefaults();
            var okResult = Assert.IsType<OkObjectResult>(result);
            var config = Assert.IsType<ChatCommandConfiguration>(okResult.Value);
            Assert.True(config.Commands.ContainsKey("!discord"));
        }
    }
}
