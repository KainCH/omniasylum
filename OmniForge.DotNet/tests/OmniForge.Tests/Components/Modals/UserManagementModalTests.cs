using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Modals;
using System.Threading.Tasks;
using Xunit;
using System;

namespace OmniForge.Tests.Components.Modals
{
    public class UserManagementModalTests : BunitContext
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ITwitchApiService> _mockTwitchApiService;
        private readonly MockAuthenticationStateProvider _mockAuthenticationStateProvider;

        public UserManagementModalTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockTwitchApiService = new Mock<ITwitchApiService>();
            Services.AddSingleton(_mockUserRepository.Object);
            Services.AddSingleton(_mockTwitchApiService.Object);

            _mockAuthenticationStateProvider = new MockAuthenticationStateProvider();
            Services.AddAuthorizationCore();
            Services.AddScoped<AuthenticationStateProvider>(s => _mockAuthenticationStateProvider);

            var identity = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "TestAdmin"),
                new System.Security.Claims.Claim("userId", "admin-123")
            }, "TestAuthType");
            _mockAuthenticationStateProvider.SetUser(new System.Security.Claims.ClaimsPrincipal(identity));
        }

        [Fact]
        public void Hidden_ByDefault()
        {
            var cut = Render(b =>
            {
                b.OpenComponent<UserManagementModal>(0);
                b.AddAttribute(1, nameof(UserManagementModal.Show), false);
                b.CloseComponent();
            });

            Assert.Empty(cut.FindAll(".modal"));
        }

        [Fact]
        public void Renders_WhenShowIsTrue()
        {
            var cut = Render(b =>
            {
                b.OpenComponent<UserManagementModal>(0);
                b.AddAttribute(1, nameof(UserManagementModal.Show), true);
                b.CloseComponent();
            });

            Assert.NotEmpty(cut.FindAll(".modal"));
            Assert.Contains("Create User", cut.Find(".modal-title").TextContent);
        }

        [Fact]
        public void LoadsUser_WhenUserIdProvided()
        {
            // Arrange
            var user = new User
            {
                TwitchUserId = "123",
                Username = "TestUser",
                Email = "test@example.com",
                Role = "streamer"
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            // Act
            var cut = Render(b =>
            {
                b.OpenComponent<UserManagementModal>(0);
                b.AddAttribute(1, nameof(UserManagementModal.Show), true);
                b.AddAttribute(2, nameof(UserManagementModal.UserId), "123");
                b.CloseComponent();
            });

            // Assert
            cut.WaitForState(() => cut.FindAll("input[value='TestUser']").Count > 0);
            Assert.Contains("Edit User: TestUser", cut.Find(".modal-title").TextContent);
            Assert.Equal("test@example.com", cut.Find("input[value='test@example.com']").GetAttribute("value"));
        }

        [Fact]
        public void SaveUser_CallsRepositoryAndCloses()
        {
            // Arrange
            var onSavedCalled = false;
            var showChangedCalled = false;

            _mockUserRepository.Setup(x => x.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
            _mockTwitchApiService.Setup(x => x.GetUserByLoginAsync("NewUser", It.IsAny<string>()))
                .ReturnsAsync(new OmniForge.Core.Interfaces.TwitchUserDto
                {
                    Id = "new-user-id",
                    Login = "NewUser",
                    DisplayName = "NewUser",
                    Email = "new@example.com",
                    ProfileImageUrl = "http://example.com/image.jpg"
                });

            var cut = Render(b =>
            {
                b.OpenComponent<UserManagementModal>(0);
                b.AddAttribute(1, nameof(UserManagementModal.Show), true);
                b.AddAttribute(2, nameof(UserManagementModal.OnSaved), EventCallback.Factory.Create(this, () => onSavedCalled = true));
                b.AddAttribute(3, nameof(UserManagementModal.ShowChanged), EventCallback.Factory.Create<bool>(this, val => showChangedCalled = true));
                b.CloseComponent();
            });

            // Act
            // Since it's a new user, it's enabled.
            // Re-find elements after each change to avoid stale references
            cut.FindAll("input.form-control")[0].Change("NewUser"); // Username
            cut.FindAll("input.form-control")[1].Change("new@example.com"); // Email

            cut.Find("form").Submit();

            // Assert
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Username == "NewUser" && u.Email == "new@example.com")), Times.Once);
            Assert.True(onSavedCalled);
            Assert.True(showChangedCalled);
        }

        [Fact]
        public void TogglingFeature_UpdatesModel()
        {
            // Arrange
            var user = new User { TwitchUserId = "123", Features = new FeatureFlags { StreamOverlay = false } };
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            var cut = Render(b =>
            {
                b.OpenComponent<UserManagementModal>(0);
                b.AddAttribute(1, nameof(UserManagementModal.Show), true);
                b.AddAttribute(2, nameof(UserManagementModal.UserId), "123");
                b.CloseComponent();
            });

            cut.WaitForState(() => cut.FindAll(".form-check-input").Count > 0);

            // Act
            // Let's just find all checkboxes and toggle one.
            var checkboxes = cut.FindAll(".card .form-check-input");
            // Assuming StreamOverlay is one of them.
            checkboxes[0].Change(true);

            cut.Find("form").Submit();

            // Assert
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Features.StreamOverlay == true || u.Features.ChatCommands == true || u.Features.DiscordNotifications == true)), Times.Once);
        }
    }

    public class MockAuthenticationStateProvider : Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider
    {
        private Microsoft.AspNetCore.Components.Authorization.AuthenticationState _authState;

        public MockAuthenticationStateProvider()
        {
            _authState = new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()));
        }

        public void SetUser(System.Security.Claims.ClaimsPrincipal user)
        {
            _authState = new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(user);
            NotifyAuthenticationStateChanged(Task.FromResult(_authState));
        }

        public override Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(_authState);
        }
    }
}
