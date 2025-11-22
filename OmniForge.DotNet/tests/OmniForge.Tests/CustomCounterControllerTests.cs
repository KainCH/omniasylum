using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using OmniForge.Web.Hubs;
using Xunit;

namespace OmniForge.Tests
{
    public class CustomCounterControllerTests
    {
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IHubContext<OverlayHub>> _mockHubContext;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<IClientProxy> _mockClientProxy;
        private readonly Mock<IHubClients> _mockHubClients;
        private readonly CustomCounterController _controller;

        public CustomCounterControllerTests()
        {
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockHubContext = new Mock<IHubContext<OverlayHub>>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockClientProxy = new Mock<IClientProxy>();
            _mockHubClients = new Mock<IHubClients>();

            // Setup Hub Context Mocks
            _mockHubContext.Setup(x => x.Clients).Returns(_mockHubClients.Object);
            _mockHubClients.Setup(x => x.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);

            _controller = new CustomCounterController(
                _mockCounterRepository.Object,
                _mockUserRepository.Object,
                _mockHubContext.Object,
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

        [Fact]
        public async Task GetCustomCounters_ShouldReturnOk_WhenAuthenticated()
        {
            var config = new CustomCounterConfiguration();
            _mockCounterRepository.Setup(x => x.GetCustomCountersConfigAsync("12345"))
                .ReturnsAsync(config);

            var result = await _controller.GetCustomCounters();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(config, okResult.Value);
        }

        [Fact]
        public async Task SaveCustomCounters_ShouldReturnOk_WhenValid()
        {
            var config = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    { "c1", new CustomCounterDefinition { Name = "C1", Icon = "icon" } }
                }
            };

            var result = await _controller.SaveCustomCounters(config);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCustomCountersConfigAsync("12345", config), Times.Once);
            _mockClientProxy.Verify(x => x.SendCoreAsync("customCountersUpdated", It.IsAny<object[]>(), default), Times.Once);
        }

        [Fact]
        public async Task IncrementCounter_ShouldReturnOk_WhenValid()
        {
            var config = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    { "c1", new CustomCounterDefinition { Name = "C1", Icon = "icon", IncrementBy = 1 } }
                }
            };
            _mockCounterRepository.Setup(x => x.GetCustomCountersConfigAsync("12345"))
                .ReturnsAsync(config);

            var counter = new Counter { CustomCounters = new Dictionary<string, int> { { "c1", 5 } } };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(new Counter { CustomCounters = new Dictionary<string, int> { { "c1", 4 } } });
            _mockCounterRepository.Setup(x => x.IncrementCounterAsync("12345", "c1", 1))
                .ReturnsAsync(counter);

            var result = await _controller.IncrementCounter("c1");

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", counter), Times.Once);
        }

        [Fact]
        public async Task DecrementCounter_ShouldReturnOk_WhenValid()
        {
            var config = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    { "c1", new CustomCounterDefinition { Name = "C1", Icon = "icon", DecrementBy = 1 } }
                }
            };
            _mockCounterRepository.Setup(x => x.GetCustomCountersConfigAsync("12345"))
                .ReturnsAsync(config);

            var counter = new Counter { CustomCounters = new Dictionary<string, int> { { "c1", 3 } } };
            _mockCounterRepository.Setup(x => x.DecrementCounterAsync("12345", "c1", 1))
                .ReturnsAsync(counter);

            var result = await _controller.DecrementCounter("c1");

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", counter), Times.Once);
        }

        [Fact]
        public async Task ResetCounter_ShouldReturnOk_WhenValid()
        {
            var config = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    { "c1", new CustomCounterDefinition { Name = "C1", Icon = "icon" } }
                }
            };
            _mockCounterRepository.Setup(x => x.GetCustomCountersConfigAsync("12345"))
                .ReturnsAsync(config);

            var counter = new Counter { CustomCounters = new Dictionary<string, int> { { "c1", 0 } } };
            _mockCounterRepository.Setup(x => x.ResetCounterAsync("12345", "c1"))
                .ReturnsAsync(counter);

            var result = await _controller.ResetCounter("c1");

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", counter), Times.Once);
        }
    }
}
