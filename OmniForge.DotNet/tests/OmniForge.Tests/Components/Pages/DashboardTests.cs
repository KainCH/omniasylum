using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using OmniForge.Web.Components.Pages;
using Xunit;

#pragma warning disable CS0618

namespace OmniForge.Tests.Components.Pages
{
    public class DashboardTests : TestContext
    {
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IStreamMonitorService> _mockStreamMonitorService;
        private readonly MockAuthenticationStateProvider _authProvider;
        private readonly Mock<IAuthorizationService> _mockAuthorizationService;

        public DashboardTests()
        {
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockStreamMonitorService = new Mock<IStreamMonitorService>();
            _authProvider = new MockAuthenticationStateProvider();
            _mockAuthorizationService = new Mock<IAuthorizationService>();

            Services.AddSingleton(_mockCounterRepository.Object);
            Services.AddSingleton(_mockUserRepository.Object);
            Services.AddSingleton(_mockStreamMonitorService.Object);
            Services.AddSingleton<AuthenticationStateProvider>(_authProvider);

            // Add core authorization services (PolicyProvider, etc.)
            Services.AddAuthorizationCore();
            // Override IAuthorizationService with our mock to avoid BUnit's placeholder or default behavior
            Services.AddSingleton(_mockAuthorizationService.Object);

            // Default authorization setup
            _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());

            _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
                .ReturnsAsync(AuthorizationResult.Success());

            // Default user repository setup - returns user with default settings
            _mockUserRepository.Setup(x => x.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { TwitchUserId = "12345", OverlaySettings = new OverlaySettings() });

            // Stub out child components to avoid setting up their dependencies
            ComponentFactories.AddStub<OverlaySettingsModal>();
            ComponentFactories.AddStub<AlertEffectsModal>();
            ComponentFactories.AddStub<DiscordWebhookSettingsModal>();
            ComponentFactories.AddStub<AlertsManagerModal>();
        }

        private IRenderedComponent<Dashboard> RenderDashboard()
        {
            var cut = Render(b =>
            {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder =>
                {
                    builder.OpenComponent<Dashboard>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });
            return cut.FindComponent<Dashboard>();
        }

        [Fact]
        public void Dashboard_ShouldRenderLoading_WhenCounterIsNull()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync((Counter?)null!);

            // Act
            var cut = RenderDashboard();

            // Assert
            cut.Find("p em").MarkupMatches("<em>Loading counters...</em>");
        }

        [Fact]
        public void Dashboard_ShouldRenderCounters_WhenLoaded()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 5, Swears = 3, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            // Act
            var cut = RenderDashboard();

            // Assert - Dashboard shows counter values as read-only (no buttons)
            cut.Find(".counter-box.deaths .counter-value").MarkupMatches("<div class=\"counter-value\">5</div>");
            cut.Find(".counter-box.swears .counter-value").MarkupMatches("<div class=\"counter-value\">3</div>");
        }

        [Fact]
        public void Dashboard_ShouldRespectOverlaySettings_WhenCountersDisabled()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 5, Swears = 3, Screams = 2, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            // Configure settings to disable Deaths counter
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "12345",
                    OverlaySettings = new OverlaySettings
                    {
                        Counters = new OverlayCounters { Deaths = false, Swears = true, Screams = true }
                    }
                });

            // Act
            var cut = RenderDashboard();

            // Assert - Deaths should not be visible, Swears and Screams should be
            Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".counter-box.deaths"));
            cut.Find(".counter-box.swears .counter-value").MarkupMatches("<div class=\"counter-value\">3</div>");
            cut.Find(".counter-box.screams .counter-value").MarkupMatches("<div class=\"counter-value\">2</div>");
        }

        [Fact]
        public void Dashboard_ShouldShowBitsCounter_WhenEnabled()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 5, Swears = 3, Screams = 2, Bits = 100, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "12345",
                    OverlaySettings = new OverlaySettings
                    {
                        Counters = new OverlayCounters { Deaths = true, Swears = true, Screams = true, Bits = true }
                    }
                });

            // Act
            var cut = RenderDashboard();

            // Assert
            cut.Find(".counter-box.bits .counter-value").MarkupMatches("<div class=\"counter-value\">100</div>");
        }

        [Fact]
        public void Dashboard_ShouldHideBitsCounter_WhenDisabled()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 5, Swears = 3, Screams = 2, Bits = 100, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "12345",
                    OverlaySettings = new OverlaySettings
                    {
                        Counters = new OverlayCounters { Deaths = true, Swears = true, Screams = true, Bits = false }
                    }
                });

            // Act
            var cut = RenderDashboard();

            // Assert
            Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".counter-box.bits"));
        }

        [Fact]
        public void Dashboard_ShouldShowTotalEvents()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 10, Swears = 5, Screams = 3, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            // Act
            var cut = RenderDashboard();

            // Assert - Total events should be Deaths + Swears = 15
            var statsGrid = cut.Find(".stats-grid");
            Assert.Contains("15", statsGrid.TextContent);
        }

        [Fact]
        public void Dashboard_ShouldShowOverlayUrl()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 0, Swears = 0, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            // Act
            var cut = RenderDashboard();

            // Assert
            var content = cut.Markup;
            Assert.Contains("overlay.html?userId=12345", content);
        }

        [Fact]
        public void Dashboard_ShouldShowMonitoringStatus_WhenConnected()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 0, Swears = 0, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            _mockStreamMonitorService.Setup(x => x.IsUserSubscribed("12345"))
                .Returns(true);
            _mockStreamMonitorService.Setup(x => x.GetUserConnectionStatus("12345"))
                .Returns(new StreamMonitorStatus
                {
                    Connected = true,
                    Subscriptions = new[] { "channel.cheer", "channel.follow" }
                });

            // Act
            var cut = RenderDashboard();

            // Assert
            var statusCard = cut.Find(".monitor-status-card");
            Assert.Contains("Connected", statusCard.TextContent);
            Assert.Contains("channel.cheer", statusCard.TextContent);
        }

        [Fact]
        public void Dashboard_ShouldShowDisconnectedStatus()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 0, Swears = 0, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            _mockStreamMonitorService.Setup(x => x.IsUserSubscribed("12345"))
                .Returns(false);
            _mockStreamMonitorService.Setup(x => x.GetUserConnectionStatus("12345"))
                .Returns(new StreamMonitorStatus
                {
                    Connected = false,
                    Subscriptions = Array.Empty<string>()
                });

            // Act
            var cut = RenderDashboard();

            // Assert
            var statusCard = cut.Find(".monitor-status-card");
            Assert.Contains("Disconnected", statusCard.TextContent);
        }

        [Fact]
        public void Dashboard_ShouldShowStartMonitorButton_WhenNotMonitoring()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 0, Swears = 0, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            _mockStreamMonitorService.Setup(x => x.IsUserSubscribed("12345"))
                .Returns(false);

            // Act
            var cut = RenderDashboard();

            // Assert
            var buttons = cut.FindAll("button");
            Assert.Contains(buttons, b => b.TextContent.Contains("Start Monitor"));
        }

        [Fact]
        public void Dashboard_ShouldShowStopMonitorButton_WhenMonitoring()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 0, Swears = 0, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            _mockStreamMonitorService.Setup(x => x.IsUserSubscribed("12345"))
                .Returns(true);

            // Act
            var cut = RenderDashboard();

            // Assert
            var buttons = cut.FindAll("button");
            Assert.Contains(buttons, b => b.TextContent.Contains("Stop Monitor"));
        }

        [Fact]
        public void Dashboard_ShouldShowLastDiscordNotification_WhenPresent()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 0, Swears = 0, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            var lastNotification = DateTimeOffset.UtcNow.AddMinutes(-5);
            _mockStreamMonitorService.Setup(x => x.GetUserConnectionStatus("12345"))
                .Returns(new StreamMonitorStatus
                {
                    Connected = true,
                    LastDiscordNotification = lastNotification,
                    LastDiscordNotificationSuccess = true,
                    Subscriptions = Array.Empty<string>()
                });

            // Act
            var cut = RenderDashboard();

            // Assert
            var statusCard = cut.Find(".monitor-status-card");
            Assert.DoesNotContain("Never", statusCard.TextContent.Substring(statusCard.TextContent.IndexOf("Last Discord Notification")));
        }

        [Fact]
        public async Task Dashboard_ToggleMonitoring_ShouldStartMonitor()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 0, Swears = 0, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            _mockStreamMonitorService.Setup(x => x.IsUserSubscribed("12345"))
                .Returns(false);
            _mockStreamMonitorService.Setup(x => x.SubscribeToUserAsync("12345"))
                .ReturnsAsync(SubscriptionResult.Success);

            var cut = RenderDashboard();

            // Act
            var startButton = cut.FindAll("button").First(b => b.TextContent.Contains("Start Monitor"));
            await cut.InvokeAsync(() => startButton.Click());

            // Assert
            _mockStreamMonitorService.Verify(x => x.SubscribeToUserAsync("12345"), Times.Once);
        }

        [Fact]
        public async Task Dashboard_ToggleMonitoring_ShouldStopMonitor()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 0, Swears = 0, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            _mockStreamMonitorService.Setup(x => x.IsUserSubscribed("12345"))
                .Returns(true);

            var cut = RenderDashboard();

            // Act
            var stopButton = cut.FindAll("button").First(b => b.TextContent.Contains("Stop Monitor"));
            await cut.InvokeAsync(() => stopButton.Click());

            // Assert
            _mockStreamMonitorService.Verify(x => x.UnsubscribeFromUserAsync("12345"), Times.Once);
        }
    }

    public class MockAuthenticationStateProvider : AuthenticationStateProvider
    {
        private AuthenticationState _authState;

        public MockAuthenticationStateProvider()
        {
            _authState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public void SetUser(ClaimsPrincipal user)
        {
            _authState = new AuthenticationState(user);
            NotifyAuthenticationStateChanged(Task.FromResult(_authState));
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(_authState);
        }
    }
}
