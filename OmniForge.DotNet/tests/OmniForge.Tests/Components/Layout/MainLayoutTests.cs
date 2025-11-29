using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using OmniForge.Web.Components.Layout;
using Xunit;

namespace OmniForge.Tests.Components.Layout;

public class MainLayoutTests : BunitContext
{
    private readonly Mock<IJSRuntime> _mockJSRuntime;

    public MainLayoutTests()
    {
        _mockJSRuntime = new Mock<IJSRuntime>();
        Services.AddSingleton(_mockJSRuntime.Object);

        Services.AddOptions();

        // Remove BUnit's placeholder service which throws exceptions
        var descriptor = Services.FirstOrDefault(d => d.ServiceType == typeof(Microsoft.AspNetCore.Authorization.IAuthorizationService));
        if (descriptor != null)
        {
            Services.Remove(descriptor);
        }

        Services.AddAuthorizationCore();
        Services.AddScoped<AuthenticationStateProvider, TestAuthStateProvider>();

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Layout_ShouldRenderCorrectly()
    {
        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(b2 =>
            {
                b2.OpenComponent<MainLayout>(2);
                b2.AddAttribute(3, "Body", (RenderFragment)(b3 => b3.AddMarkupContent(4, "<div>Body</div>")));
                b2.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        cut.MarkupMatches(@"
            <div class=""page"">
                <div class=""sidebar"">
                  <div class=""top-row ps-3 navbar navbar-dark"">
                    <div class=""container-fluid"">
                      <span class=""navbar-brand"">OmniForge</span>
                    </div>
                  </div>
                  <input type=""checkbox"" title=""Navigation menu"" class=""navbar-toggler"" />
                  <div class=""nav-scrollable"" onclick=""document.querySelector('.navbar-toggler').click()"">
                    <nav class=""nav flex-column"">
                      <div class=""nav-item px-3"">
                        <a href="""" class=""nav-link active"" aria-current=""page"">
                          <span class=""bi bi-house-door-fill-nav-menu"" aria-hidden=""true""></span>
                          Home
                        </a>
                      </div>
                    </nav>
                  </div>
                </div>
                <main>
                    <div class=""top-row px-4"">
                        <button class=""btn btn-link me-3"">
                            <span class=""bi bi-moon-fill"" aria-hidden=""true""></span>
                            Dark Mode
                        </button>
                        <a href=""auth/twitch"">Log in with Twitch</a>
                        <a href=""https://learn.microsoft.com/aspnet/core/"" target=""_blank"" class=""ms-3"">About</a>
                    </div>
                    <article class=""content px-4"">
                        <div>Body</div>
                    </article>
                </main>
            </div>
            <div id=""blazor-error-ui"" data-nosnippet="""">
                An unhandled error has occurred.
                <a href=""."" class=""reload"">Reload</a>
                <span class=""dismiss"">🗙</span>
            </div>
        ");
    }

    [Fact]
    public void ToggleDarkMode_ShouldCallJS_WhenClicked()
    {
        // Arrange
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(b2 =>
            {
                b2.OpenComponent<MainLayout>(2);
                b2.AddAttribute(3, "Body", (RenderFragment)(b3 => b3.AddMarkupContent(4, "<div>Body</div>")));
                b2.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Act
        cut.Find("button").Click();

        // Assert
        _mockJSRuntime.Verify(x => x.InvokeAsync<object>("toggleDarkMode", It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public void Layout_ShouldInitializeDarkMode_FromJS()
    {
        // Arrange
        _mockJSRuntime.Setup(x => x.InvokeAsync<string>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync("dark");

        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<CascadingAuthenticationState>(0);
            b.AddAttribute(1, "ChildContent", (RenderFragment)(b2 =>
            {
                b2.OpenComponent<MainLayout>(2);
                b2.AddAttribute(3, "Body", (RenderFragment)(b3 => b3.AddMarkupContent(4, "<div>Body</div>")));
                b2.CloseComponent();
            }));
            b.CloseComponent();
        });

        // Assert
        _mockJSRuntime.Verify(x => x.InvokeAsync<object>("initDarkMode", It.IsAny<object[]>()), Times.Once);
    }
}

public class TestAuthStateProvider : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
