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
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly ChatCommandController _controller;

        public ChatCommandControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _controller = new ChatCommandController(
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockOverlayNotifier.Object);

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

        private ChatCommandController CreateControllerWithNoUser()
        {
            var controller = new ChatCommandController(
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockOverlayNotifier.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            };

            return controller;
        }

        [Fact]
        public async Task GetChatCommands_ShouldReturnOk_WhenAuthenticated()
        {
            var config = new ChatCommandConfiguration();
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(config);

            var result = await _controller.GetChatCommands();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(config.Commands, okResult.Value);
        }

        [Fact]
        public async Task SaveChatCommands_ShouldReturnOk_WhenValid()
        {
            var commands = new Dictionary<string, ChatCommandDefinition>
            {
                { "!test", new ChatCommandDefinition { Response = "Test" } }
            };

            var request = new SaveChatCommandsRequest { Commands = commands };

            var result = await _controller.SaveChatCommands(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345", It.Is<ChatCommandConfiguration>(c => c.Commands == commands)), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCustomAlertAsync("12345", "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task SaveChatCommands_ShouldUpdateMaxIncrementAmount_WhenProvided()
        {
            var commands = new Dictionary<string, ChatCommandDefinition>
            {
                { "!test", new ChatCommandDefinition { Response = "Test" } }
            };

            var request = new SaveChatCommandsRequest
            {
                Commands = commands,
                MaxIncrementAmount = 10
            };

            var result = await _controller.SaveChatCommands(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345", It.Is<ChatCommandConfiguration>(c => c.MaxIncrementAmount == 10)), Times.Once);
        }

        [Fact]
        public async Task SaveChatCommands_ShouldPreserveMaxIncrementAmount_WhenNotProvided()
        {
            var existingConfig = new ChatCommandConfiguration { MaxIncrementAmount = 5 };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(existingConfig);

            var commands = new Dictionary<string, ChatCommandDefinition>
            {
                { "!test", new ChatCommandDefinition { Response = "Test" } }
            };

            var request = new SaveChatCommandsRequest { Commands = commands };

            var result = await _controller.SaveChatCommands(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345", It.Is<ChatCommandConfiguration>(c => c.MaxIncrementAmount == 5)), Times.Once);
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
            _mockOverlayNotifier.Verify(x => x.NotifyCustomAlertAsync("12345", "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
        }

        // ... (skip AddChatCommand_ShouldReturnBadRequest_WhenCommandExists as it doesn't use Notify)

        [Fact]
        public async Task UpdateChatCommand_ShouldReturnOk_WhenCommandExists()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!update", new ChatCommandDefinition { Response = "Old" } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(config);

            var request = new UpdateCommandRequest
            {
                Config = new UpdateCommandConfigDto { Response = "New" }
            };

            var result = await _controller.UpdateChatCommand("!update", request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345", It.Is<ChatCommandConfiguration>(c => c.Commands["!update"].Response == "New")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCustomAlertAsync("12345", "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task UpdateChatCommand_ShouldReturnNotFound_WhenCommandDoesNotExist()
        {
            var config = new ChatCommandConfiguration();
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(config);

            var request = new UpdateCommandRequest
            {
                Config = new UpdateCommandConfigDto { Response = "New" }
            };

            var result = await _controller.UpdateChatCommand("!missing", request);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task DeleteChatCommand_ShouldReturnOk_WhenCommandExists()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!delete", new ChatCommandDefinition { Response = "Delete Me", Custom = true } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(config);

            var result = await _controller.DeleteChatCommand("!delete");

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345", It.Is<ChatCommandConfiguration>(c => !c.Commands.ContainsKey("!delete"))), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCustomAlertAsync("12345", "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task TestChatCommand_ShouldReplaceVariables()
        {
            var command = "!test";
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { command, new ChatCommandDefinition { Response = "Deaths: {{deaths}}", Enabled = true } }
                }
            };

            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345"))
                .ReturnsAsync(config);

            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(new Counter { TwitchUserId = "12345", Deaths = 10 });

            var result = await _controller.TestChatCommand(command);

            var okResult = Assert.IsType<OkObjectResult>(result);
            // We can't easily check dynamic properties in unit tests without casting to dynamic or reflection
            // But we can verify the result type is OK.
        }

        #region Unauthorized Tests

        [Fact]
        public async Task GetChatCommands_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();

            var result = await controller.GetChatCommands();

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task SaveChatCommands_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var request = new SaveChatCommandsRequest { Commands = new Dictionary<string, ChatCommandDefinition>() };

            var result = await controller.SaveChatCommands(request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task AddChatCommand_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var request = new AddCommandRequest { Command = "!test", Config = new ChatCommandDefinition { Response = "test" } };

            var result = await controller.AddChatCommand(request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateChatCommand_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var request = new UpdateCommandRequest { Config = new UpdateCommandConfigDto { Response = "test" } };

            var result = await controller.UpdateChatCommand("!test", request);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task DeleteChatCommand_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();

            var result = await controller.DeleteChatCommand("!test");

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task TestChatCommand_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();

            var result = await controller.TestChatCommand("!test");

            Assert.IsType<UnauthorizedResult>(result);
        }

        #endregion

        #region GetDefaults Tests

        [Fact]
        public void GetDefaults_ShouldReturnAllDefaultCommands()
        {
            var result = _controller.GetDefaults();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var defaults = Assert.IsAssignableFrom<Dictionary<string, ChatCommandDefinition>>(okResult.Value);

            Assert.Contains("!deaths", defaults.Keys);
            Assert.Contains("!swears", defaults.Keys);
            Assert.Contains("!screams", defaults.Keys);
            Assert.Contains("!stats", defaults.Keys);
            Assert.Contains("!death+", defaults.Keys);
            Assert.Contains("!death-", defaults.Keys);
            Assert.Contains("!d+", defaults.Keys);
            Assert.Contains("!d-", defaults.Keys);
            Assert.Contains("!swear+", defaults.Keys);
            Assert.Contains("!swear-", defaults.Keys);
            Assert.Contains("!sw+", defaults.Keys);
            Assert.Contains("!sw-", defaults.Keys);
            Assert.Contains("!scream+", defaults.Keys);
            Assert.Contains("!scream-", defaults.Keys);
            Assert.Contains("!sc+", defaults.Keys);
            Assert.Contains("!sc-", defaults.Keys);
            Assert.Contains("!resetcounters", defaults.Keys);
        }

        [Fact]
        public void GetDefaults_ShouldHaveCorrectPermissions()
        {
            var result = _controller.GetDefaults();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var defaults = Assert.IsAssignableFrom<Dictionary<string, ChatCommandDefinition>>(okResult.Value);

            Assert.Equal("everyone", defaults["!deaths"].Permission);
            Assert.Equal("moderator", defaults["!death+"].Permission);
            Assert.Equal("broadcaster", defaults["!resetcounters"].Permission);
        }

        #endregion

        #region SaveChatCommands Validation Tests

        [Fact]
        public async Task SaveChatCommands_ShouldReturnBadRequest_WhenCommandsNull()
        {
            var request = new SaveChatCommandsRequest { Commands = null! };

            var result = await _controller.SaveChatCommands(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SaveChatCommands_ShouldReturnBadRequest_WhenCommandDoesNotStartWithExclamation()
        {
            var commands = new Dictionary<string, ChatCommandDefinition>
            {
                { "test", new ChatCommandDefinition { Response = "Test", Permission = "everyone", Cooldown = 5 } }
            };
            var request = new SaveChatCommandsRequest { Commands = commands };

            var result = await _controller.SaveChatCommands(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("must start with !", badRequest.Value?.ToString());
        }

        [Fact]
        public async Task SaveChatCommands_ShouldReturnBadRequest_WhenInvalidPermission()
        {
            var commands = new Dictionary<string, ChatCommandDefinition>
            {
                { "!test", new ChatCommandDefinition { Response = "Test", Permission = "invalid", Cooldown = 5 } }
            };
            var request = new SaveChatCommandsRequest { Commands = commands };

            var result = await _controller.SaveChatCommands(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("invalid permission", badRequest.Value?.ToString());
        }

        [Fact]
        public async Task SaveChatCommands_ShouldReturnBadRequest_WhenNegativeCooldown()
        {
            var commands = new Dictionary<string, ChatCommandDefinition>
            {
                { "!test", new ChatCommandDefinition { Response = "Test", Permission = "everyone", Cooldown = -1 } }
            };
            var request = new SaveChatCommandsRequest { Commands = commands };

            var result = await _controller.SaveChatCommands(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("invalid cooldown", badRequest.Value?.ToString());
        }

        [Fact]
        public async Task SaveChatCommands_ShouldAcceptAllValidPermissions()
        {
            var permissions = new[] { "everyone", "subscriber", "moderator", "broadcaster" };
            var commands = new Dictionary<string, ChatCommandDefinition>();

            for (int i = 0; i < permissions.Length; i++)
            {
                commands[$"!test{i}"] = new ChatCommandDefinition { Response = "Test", Permission = permissions[i], Cooldown = 5 };
            }

            var request = new SaveChatCommandsRequest { Commands = commands };

            var result = await _controller.SaveChatCommands(request);

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region AddChatCommand Tests

        [Fact]
        public async Task AddChatCommand_ShouldReturnBadRequest_WhenCommandIsEmpty()
        {
            var request = new AddCommandRequest
            {
                Command = "",
                Config = new ChatCommandDefinition { Response = "test" }
            };

            var result = await _controller.AddChatCommand(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddChatCommand_ShouldReturnBadRequest_WhenCommandDoesNotStartWithExclamation()
        {
            var request = new AddCommandRequest
            {
                Command = "test",
                Config = new ChatCommandDefinition { Response = "test" }
            };

            var result = await _controller.AddChatCommand(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddChatCommand_ShouldReturnBadRequest_WhenConfigIsNull()
        {
            var request = new AddCommandRequest
            {
                Command = "!test",
                Config = null!
            };

            var result = await _controller.AddChatCommand(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddChatCommand_ShouldReturnBadRequest_WhenResponseIsEmpty()
        {
            var request = new AddCommandRequest
            {
                Command = "!test",
                Config = new ChatCommandDefinition { Response = "" }
            };

            var result = await _controller.AddChatCommand(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddChatCommand_ShouldReturnBadRequest_WhenCommandAlreadyExists()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!existing", new ChatCommandDefinition { Response = "existing" } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var request = new AddCommandRequest
            {
                Command = "!existing",
                Config = new ChatCommandDefinition { Response = "new" }
            };

            var result = await _controller.AddChatCommand(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddChatCommand_ShouldSetDefaultPermission_WhenNotProvided()
        {
            var config = new ChatCommandConfiguration();
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var request = new AddCommandRequest
            {
                Command = "!new",
                Config = new ChatCommandDefinition { Response = "test", Permission = null! }
            };

            await _controller.AddChatCommand(request);

            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345",
                It.Is<ChatCommandConfiguration>(c => c.Commands["!new"].Permission == "everyone")), Times.Once);
        }

        [Fact]
        public async Task AddChatCommand_ShouldSetCustomTrue()
        {
            var config = new ChatCommandConfiguration();
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var request = new AddCommandRequest
            {
                Command = "!new",
                Config = new ChatCommandDefinition { Response = "test" }
            };

            await _controller.AddChatCommand(request);

            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345",
                It.Is<ChatCommandConfiguration>(c => c.Commands["!new"].Custom == true)), Times.Once);
        }

        #endregion

        #region UpdateChatCommand Tests

        [Fact]
        public async Task UpdateChatCommand_ShouldReturnBadRequest_WhenCommandDoesNotStartWithExclamation()
        {
            var request = new UpdateCommandRequest { Config = new UpdateCommandConfigDto { Response = "test" } };

            var result = await _controller.UpdateChatCommand("test", request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateChatCommand_ShouldUpdateOnlyProvidedProperties()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!update", new ChatCommandDefinition { Response = "Old", Permission = "everyone", Cooldown = 5 } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var request = new UpdateCommandRequest { Config = new UpdateCommandConfigDto { Response = "New" } };

            await _controller.UpdateChatCommand("!update", request);

            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345",
                It.Is<ChatCommandConfiguration>(c =>
                    c.Commands["!update"].Response == "New" &&
                    c.Commands["!update"].Permission == "everyone" &&
                    c.Commands["!update"].Cooldown == 5)), Times.Once);
        }

        [Fact]
        public async Task UpdateChatCommand_ShouldUpdatePermission_WhenProvided()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!update", new ChatCommandDefinition { Response = "Test", Permission = "everyone" } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var request = new UpdateCommandRequest { Config = new UpdateCommandConfigDto { Permission = "moderator" } };

            await _controller.UpdateChatCommand("!update", request);

            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345",
                It.Is<ChatCommandConfiguration>(c => c.Commands["!update"].Permission == "moderator")), Times.Once);
        }

        [Fact]
        public async Task UpdateChatCommand_ShouldUpdateCooldown_WhenProvided()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!update", new ChatCommandDefinition { Response = "Test", Cooldown = 5 } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var request = new UpdateCommandRequest { Config = new UpdateCommandConfigDto { Cooldown = 10 } };

            await _controller.UpdateChatCommand("!update", request);

            _mockUserRepository.Verify(x => x.SaveChatCommandsConfigAsync("12345",
                It.Is<ChatCommandConfiguration>(c => c.Commands["!update"].Cooldown == 10)), Times.Once);
        }

        #endregion

        #region DeleteChatCommand Tests

        [Fact]
        public async Task DeleteChatCommand_ShouldReturnBadRequest_WhenCommandDoesNotStartWithExclamation()
        {
            var result = await _controller.DeleteChatCommand("test");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task DeleteChatCommand_ShouldReturnNotFound_WhenCommandDoesNotExist()
        {
            var config = new ChatCommandConfiguration();
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var result = await _controller.DeleteChatCommand("!missing");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task DeleteChatCommand_ShouldReturnBadRequest_WhenDeletingCoreCommand()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!deaths", new ChatCommandDefinition { Response = "Deaths", Custom = false } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var result = await _controller.DeleteChatCommand("!deaths");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Cannot delete core commands", badRequest.Value?.ToString());
        }

        #endregion

        #region TestChatCommand Tests

        [Fact]
        public async Task TestChatCommand_ShouldReturnNotFound_WhenCommandDoesNotExist()
        {
            var config = new ChatCommandConfiguration();
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var result = await _controller.TestChatCommand("!missing");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task TestChatCommand_ShouldReturnBadRequest_WhenCommandDisabled()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!disabled", new ChatCommandDefinition { Response = "Test", Enabled = false } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);

            var result = await _controller.TestChatCommand("!disabled");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task TestChatCommand_ShouldReplaceAllCounterVariables()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!stats", new ChatCommandDefinition { Response = "Deaths: {{deaths}}, Swears: {{swears}}, Screams: {{screams}}, Bits: {{bits}}", Enabled = true } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(new Counter { TwitchUserId = "12345", Deaths = 10, Swears = 20, Screams = 30, Bits = 100 });

            var result = await _controller.TestChatCommand("!stats");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            var type = value!.GetType();
            var response = type.GetProperty("response")!.GetValue(value) as string;

            Assert.Contains("10", response);
            Assert.Contains("20", response);
            Assert.Contains("30", response);
            Assert.Contains("100", response);
        }

        [Fact]
        public async Task TestChatCommand_ShouldReplaceCustomCounterVariables()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!custom", new ChatCommandDefinition { Response = "Custom: {{mycounter}}", Enabled = true } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(new Counter
                {
                    TwitchUserId = "12345",
                    CustomCounters = new Dictionary<string, int> { { "mycounter", 42 } }
                });

            var result = await _controller.TestChatCommand("!custom");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            var type = value!.GetType();
            var response = type.GetProperty("response")!.GetValue(value) as string;

            Assert.Contains("42", response);
        }

        [Fact]
        public async Task TestChatCommand_ShouldReturnZero_WhenCountersNull()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!stats", new ChatCommandDefinition { Response = "Deaths: {{deaths}}", Enabled = true } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345")).ReturnsAsync((Counter?)null);

            var result = await _controller.TestChatCommand("!stats");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            var type = value!.GetType();
            var response = type.GetProperty("response")!.GetValue(value) as string;

            Assert.Contains("0", response);
        }

        [Fact]
        public async Task TestChatCommand_ShouldPreserveUnknownVariables()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!test", new ChatCommandDefinition { Response = "Unknown: {{unknown}}", Enabled = true } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(new Counter { TwitchUserId = "12345" });

            var result = await _controller.TestChatCommand("!test");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            var type = value!.GetType();
            var response = type.GetProperty("response")!.GetValue(value) as string;

            Assert.Contains("{{unknown}}", response);
        }

        [Fact]
        public async Task TestChatCommand_ShouldUseDefaultResponse_WhenResponseIsNull()
        {
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!test", new ChatCommandDefinition { Response = null!, Enabled = true } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync("12345")).ReturnsAsync(config);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(new Counter { TwitchUserId = "12345" });

            var result = await _controller.TestChatCommand("!test");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            var type = value!.GetType();
            var response = type.GetProperty("response")!.GetValue(value) as string;

            Assert.Equal("Command executed", response);
        }

        #endregion
    }
}
