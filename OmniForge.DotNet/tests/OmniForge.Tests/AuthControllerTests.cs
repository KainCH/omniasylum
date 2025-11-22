using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OmniForge.Web.Controllers;
using Xunit;
using AspNet.Security.OAuth.Twitch;

namespace OmniForge.Tests
{
    public class AuthControllerTests
    {
        private readonly AuthController _controller;
        private readonly Mock<IAuthenticationService> _mockAuthService;
        private readonly Mock<IServiceProvider> _mockServiceProvider;

        public AuthControllerTests()
        {
            _mockAuthService = new Mock<IAuthenticationService>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockServiceProvider.Setup(x => x.GetService(typeof(IAuthenticationService)))
                .Returns(_mockAuthService.Object);

            var httpContext = new DefaultHttpContext
            {
                RequestServices = _mockServiceProvider.Object
            };

            _controller = new AuthController
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };
        }

        [Fact]
        public void Login_ShouldReturnChallengeResult()
        {
            // Act
            var result = _controller.Login("/return-url");

            // Assert
            var challengeResult = Assert.IsType<ChallengeResult>(result);
            Assert.Contains(TwitchAuthenticationDefaults.AuthenticationScheme, challengeResult.AuthenticationSchemes);
            Assert.Equal("/return-url", challengeResult.Properties!.RedirectUri);
        }

        [Fact]
        public async Task Logout_ShouldSignOutAndRedirect()
        {
            // Act
            var result = await _controller.Logout("/return-url");

            // Assert
            _mockAuthService.Verify(x => x.SignOutAsync(
                It.IsAny<HttpContext>(),
                CookieAuthenticationDefaults.AuthenticationScheme,
                It.IsAny<AuthenticationProperties>()), Times.Once);

            var redirectResult = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/return-url", redirectResult.Url);
        }
    }
}
