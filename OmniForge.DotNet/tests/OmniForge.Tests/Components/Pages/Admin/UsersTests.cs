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
using OmniForge.Web.Components.Pages.Admin;
using Xunit;

#pragma warning disable CS0618

namespace OmniForge.Tests.Components.Pages.Admin
{
    public class UsersTests : TestContext
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly MockAuthenticationStateProvider _authProvider;
        private readonly Mock<IAuthorizationService> _mockAuthorizationService;

        public UsersTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _authProvider = new MockAuthenticationStateProvider();
            _mockAuthorizationService = new Mock<IAuthorizationService>();

            Services.AddSingleton(_mockUserRepository.Object);
            Services.AddSingleton<AuthenticationStateProvider>(_authProvider);

            // Add core authorization services
            Services.AddAuthorizationCore();
            // Override IAuthorizationService
            Services.AddSingleton(_mockAuthorizationService.Object);

            // Default authorization setup - allow everything by default for simplicity in unit tests
            // unless specific tests override it
            _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(AuthorizationResult.Success());

            _mockAuthorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
                .ReturnsAsync(AuthorizationResult.Success());

            // Stub out child components
            ComponentFactories.AddStub<UserManagementModal>();
            ComponentFactories.AddStub<UserRequestsModal>();
            ComponentFactories.AddStub<BrokenUserManagerModal>();
        }

        private IRenderedComponent<Users> RenderUsers()
        {
            var cut = Render(b => {
                b.OpenComponent<CascadingAuthenticationState>(0);
                b.AddAttribute(1, "ChildContent", (RenderFragment)(builder => {
                    builder.OpenComponent<Users>(2);
                    builder.CloseComponent();
                }));
                b.CloseComponent();
            });
            return cut.FindComponent<Users>();
        }

        [Fact]
        public void Users_ShouldRenderLoading_WhenUsersAreNull()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "admin"),
                new Claim(ClaimTypes.Role, "admin")
            }, "mock"));
            _authProvider.SetUser(user);

            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync((IEnumerable<User>?)null!);

            // Act
            var cut = RenderUsers();

            // Assert
            cut.Find("p em").MarkupMatches("<em>Loading users...</em>");
        }

        [Fact]
        public void Users_ShouldRenderUserList_WhenLoaded()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "admin"),
                new Claim(ClaimTypes.Role, "admin")
            }, "mock"));
            _authProvider.SetUser(user);

            var users = new List<User>
            {
                new User { TwitchUserId = "1", DisplayName = "User1", Role = "streamer", LastLogin = DateTime.UtcNow },
                new User { TwitchUserId = "2", DisplayName = "User2", Role = "admin", LastLogin = DateTime.UtcNow }
            };

            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(users);

            // Act
            var cut = RenderUsers();

            // Assert
            var rows = cut.FindAll("tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("User1", rows[0].InnerHtml);
            Assert.Contains("User2", rows[1].InnerHtml);
        }

        [Fact]
        public void Users_ShouldRefreshUsers_WhenRefreshClicked()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "admin"),
                new Claim(ClaimTypes.Role, "admin")
            }, "mock"));
            _authProvider.SetUser(user);

            var users = new List<User>();
            _mockUserRepository.Setup(x => x.GetAllUsersAsync())
                .ReturnsAsync(users);

            var cut = RenderUsers();

            // Act
            cut.Find("button.btn-primary").Click(); // Refresh button

            // Assert
            _mockUserRepository.Verify(x => x.GetAllUsersAsync(), Times.AtLeast(2)); // Once on init, once on click
        }

        [Fact]
        public void ClickingNewUser_OpensModal()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "admin"),
                new Claim(ClaimTypes.Role, "admin")
            }, "mock"));
            _authProvider.SetUser(user);
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(new List<User>());

            var cut = RenderUsers();

            // Act
            cut.Find("button.btn-success").Click(); // New User button

            // Assert
            var modal = cut.FindComponent<Stub<UserManagementModal>>();
            Assert.True(modal.Instance.Parameters.Get(x => x.Show));
            Assert.Null(modal.Instance.Parameters.Get(x => x.UserId));
        }

        [Fact]
        public void ClickingEditUser_OpensModalWithUserId()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "admin"),
                new Claim(ClaimTypes.Role, "admin")
            }, "mock"));
            _authProvider.SetUser(user);

            var users = new List<User>
            {
                new User { TwitchUserId = "123", DisplayName = "TestUser", Role = "streamer", LastLogin = DateTime.UtcNow }
            };
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(users);

            var cut = RenderUsers();

            // Act
            cut.Find("button.btn-outline-primary").Click(); // Edit button

            // Assert
            var modal = cut.FindComponent<Stub<UserManagementModal>>();
            Assert.True(modal.Instance.Parameters.Get(x => x.Show));
            Assert.Equal("123", modal.Instance.Parameters.Get(x => x.UserId));
        }

        [Fact]
        public void ClickingRequests_OpensRequestsModal()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "admin"),
                new Claim(ClaimTypes.Role, "admin")
            }, "mock"));
            _authProvider.SetUser(user);
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(new List<User>());

            var cut = RenderUsers();

            // Act
            cut.Find("button.btn-outline-info").Click(); // Requests button

            // Assert
            var modal = cut.FindComponent<Stub<UserRequestsModal>>();
            Assert.True(modal.Instance.Parameters.Get(x => x.Show));
        }

        [Fact]
        public void ClickingFixUsers_OpensBrokenUserModal()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "admin"),
                new Claim(ClaimTypes.Role, "admin")
            }, "mock"));
            _authProvider.SetUser(user);
            _mockUserRepository.Setup(x => x.GetAllUsersAsync()).ReturnsAsync(new List<User>());

            var cut = RenderUsers();

            // Act
            cut.Find("button.btn-outline-warning").Click(); // Fix Users button

            // Assert
            var modal = cut.FindComponent<Stub<BrokenUserManagerModal>>();
            Assert.True(modal.Instance.Parameters.Get(x => x.Show));
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
