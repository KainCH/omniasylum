using System.Diagnostics;
using Bunit;
using OmniForge.Web.Components.Pages;
using Xunit;

namespace OmniForge.Tests.Components.Pages;

public class ErrorTests : BunitContext
{
    [Fact]
    public void Error_ShouldRenderErrorMessage()
    {
        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<Error>(0);
            b.CloseComponent();
        });

        // Assert
        Assert.Contains("Error.", cut.Markup);
        Assert.Contains("An error occurred while processing your request", cut.Markup);
    }

    [Fact]
    public void Error_ShouldRenderDevelopmentModeSection()
    {
        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<Error>(0);
            b.CloseComponent();
        });

        // Assert
        Assert.Contains("Development Mode", cut.Markup);
        Assert.Contains("Development environment", cut.Markup);
    }

    [Fact]
    public void Error_ShouldShowRequestId_WhenActivityExists()
    {
        // Arrange
        var activity = new Activity("TestActivity");
        activity.Start();

        try
        {
            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<Error>(0);
                b.CloseComponent();
            });

            // Assert
            Assert.Contains("Request ID:", cut.Markup);
            Assert.Contains(activity.Id!, cut.Markup);
        }
        finally
        {
            activity.Stop();
        }
    }

    [Fact]
    public void Error_ShouldContainWarningAboutProductionEnvironment()
    {
        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<Error>(0);
            b.CloseComponent();
        });

        // Assert
        Assert.Contains("shouldn't be enabled for deployed applications", cut.Markup);
        Assert.Contains("sensitive information", cut.Markup);
    }

    [Fact]
    public void Error_ShouldMentionAspNetCoreEnvironmentVariable()
    {
        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<Error>(0);
            b.CloseComponent();
        });

        // Assert
        Assert.Contains("ASPNETCORE_ENVIRONMENT", cut.Markup);
    }

    [Fact]
    public void Error_ShouldHaveTextDangerClass()
    {
        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<Error>(0);
            b.CloseComponent();
        });

        // Assert
        var h1 = cut.Find("h1.text-danger");
        Assert.Contains("Error.", h1.TextContent);
    }

    [Fact]
    public void Error_ShouldRenderH2WithErrorDescription()
    {
        // Act
        var cut = Render(b =>
        {
            b.OpenComponent<Error>(0);
            b.CloseComponent();
        });

        // Assert
        var h2 = cut.Find("h2.text-danger");
        Assert.Contains("An error occurred while processing your request", h2.TextContent);
    }
}
