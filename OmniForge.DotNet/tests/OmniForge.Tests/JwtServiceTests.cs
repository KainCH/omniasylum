using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class JwtServiceTests
    {
        private readonly Mock<IOptions<JwtSettings>> _mockOptions;
        private readonly JwtService _service;
        private readonly JwtSettings _settings;

        public JwtServiceTests()
        {
            _settings = new JwtSettings
            {
                Secret = "super_secret_key_for_testing_purposes_only_must_be_long_enough",
                ExpiryDays = 1,
                Issuer = "TestIssuer",
                Audience = "TestAudience"
            };

            _mockOptions = new Mock<IOptions<JwtSettings>>();
            _mockOptions.Setup(x => x.Value).Returns(_settings);

            _service = new JwtService(_mockOptions.Object);
        }

        [Fact]
        public void GenerateToken_ShouldReturnValidToken()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Username = "testuser",
                DisplayName = "Test User",
                Role = "streamer"
            };

            var token = _service.GenerateToken(user);

            Assert.NotNull(token);
            Assert.NotEmpty(token);

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            Assert.Equal(_settings.Issuer, jwtToken.Issuer);
            Assert.Contains(_settings.Audience, jwtToken.Audiences);
            Assert.Contains(jwtToken.Claims, c => c.Type == "userId" && c.Value == "12345");
        }

        [Fact]
        public void ValidateToken_ShouldReturnPrincipal_WhenTokenIsValid()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                Username = "testuser",
                DisplayName = "Test User",
                Role = "streamer"
            };

            var token = _service.GenerateToken(user);

            var principal = _service.ValidateToken(token);

            Assert.NotNull(principal);
            Assert.Equal("12345", principal.FindFirst("userId")?.Value);
        }

        [Fact]
        public void ValidateToken_ShouldReturnNull_WhenTokenIsExpired()
        {
            // Create a service with normal settings
            var settings = new JwtSettings
            {
                Secret = "super_secret_key_for_testing_purposes_only_must_be_long_enough",
                ExpiryDays = 1,
                Issuer = "TestIssuer",
                Audience = "TestAudience"
            };
            var mockOptions = new Mock<IOptions<JwtSettings>>();
            mockOptions.Setup(x => x.Value).Returns(settings);
            var service = new JwtService(mockOptions.Object);

            // Manually create an expired token
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = System.Text.Encoding.ASCII.GetBytes(settings.Secret);
            var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("userId", "12345") }),
                Expires = DateTime.UtcNow.AddMinutes(-10), // Expired 10 mins ago
                IssuedAt = DateTime.UtcNow.AddMinutes(-20), // Issued 20 mins ago
                NotBefore = DateTime.UtcNow.AddMinutes(-20),
                Issuer = settings.Issuer,
                Audience = settings.Audience,
                SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key), Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            var principal = service.ValidateToken(tokenString);

            Assert.Null(principal);
        }
    }
}
