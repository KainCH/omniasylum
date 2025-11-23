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
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly MockAuthenticationStateProvider _authProvider;
        private readonly Mock<IAuthorizationService> _mockAuthorizationService;

        public DashboardTests()
        {
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _authProvider = new MockAuthenticationStateProvider();
            _mockAuthorizationService = new Mock<IAuthorizationService>();

            Services.AddSingleton(_mockCounterRepository.Object);
            Services.AddSingleton(_mockOverlayNotifier.Object);
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

            // Stub out child components to avoid setting up their dependencies
            ComponentFactories.AddStub<OverlaySettingsModal>();
            ComponentFactories.AddStub<AlertEffectsModal>();
            ComponentFactories.AddStub<DiscordWebhookSettingsModal>();
            ComponentFactories.AddStub<AlertsManagerModal>();
        }

        private IRenderedComponent<Dashboard> RenderDashboard()
        {
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
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

            // Assert
            cut.Find(".counter-box.deaths .counter-value").MarkupMatches("<div class=\"counter-value\">5</div>");
            cut.Find(".counter-box.swears .counter-value").MarkupMatches("<div class=\"counter-value\">3</div>");
        }

        [Fact]
        public void Dashboard_ShouldIncrementDeaths_WhenButtonClicked()
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

            // Act
            cut.Find(".counter-box.deaths .btn-increment").Click();

            // Assert
            cut.Find(".counter-box.deaths .counter-value").MarkupMatches("<div class=\"counter-value\">6</div>");
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.Deaths == 6)), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", It.Is<Counter>(c => c.Deaths == 6)), Times.Once);
        }

        [Fact]
        public void Dashboard_ShouldResetCounters_WhenResetClicked()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "12345")
            }, "mock"));
            _authProvider.SetUser(user);

            var counter = new Counter { Deaths = 5, Swears = 3, LastUpdated = DateTime.UtcNow };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counter);

            var cut = RenderDashboard();

            // Act
            cut.Find(".btn-reset").Click();

            // Assert
            cut.Find(".counter-box.deaths .counter-value").MarkupMatches("<div class=\"counter-value\">0</div>");
            cut.Find(".counter-box.swears .counter-value").MarkupMatches("<div class=\"counter-value\">0</div>");
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.Deaths == 0 && c.Swears == 0)), Times.Once);
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
