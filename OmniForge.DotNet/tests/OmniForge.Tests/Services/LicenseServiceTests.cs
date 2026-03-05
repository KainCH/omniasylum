using System;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class LicenseServiceTests
    {
        private readonly LicenseService _sut = new();

        [Fact]
        public void GetEffectiveTier_WhenNoExpiry_ReturnsActualTier()
        {
            var user = new User { LicenseTier = LicenseTier.Pro, LicenseExpiresAt = null };

            var result = _sut.GetEffectiveTier(user);

            Assert.Equal(LicenseTier.Pro, result);
        }

        [Fact]
        public void GetEffectiveTier_WhenNotExpired_ReturnsActualTier()
        {
            var user = new User
            {
                LicenseTier = LicenseTier.Premium,
                LicenseExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
            };

            var result = _sut.GetEffectiveTier(user);

            Assert.Equal(LicenseTier.Premium, result);
        }

        [Fact]
        public void GetEffectiveTier_WhenExpired_ReturnsFree()
        {
            var user = new User
            {
                LicenseTier = LicenseTier.Pro,
                LicenseExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
            };

            var result = _sut.GetEffectiveTier(user);

            Assert.Equal(LicenseTier.Free, result);
        }

        [Fact]
        public void GetEffectiveTier_WhenNullUser_ReturnsFree()
        {
            var result = _sut.GetEffectiveTier(null!);

            Assert.Equal(LicenseTier.Free, result);
        }

        [Theory]
        [InlineData(LicenseTier.Free, "StreamOverlay", true)]
        [InlineData(LicenseTier.Free, "OverlayV2", false)]
        [InlineData(LicenseTier.Pro, "OverlayV2", true)]
        [InlineData(LicenseTier.Pro, "SceneSync", false)]
        [InlineData(LicenseTier.Premium, "SceneSync", true)]
        [InlineData(LicenseTier.Premium, "Analytics", true)]
        public void HasFeatureAccess_ReturnsCorrectResult(LicenseTier tier, string feature, bool expected)
        {
            var user = new User { LicenseTier = tier, LicenseExpiresAt = null };

            var result = _sut.HasFeatureAccess(user, feature);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsLicenseActive_WhenNoExpiry_ReturnsTrue()
        {
            var user = new User { LicenseExpiresAt = null };

            Assert.True(_sut.IsLicenseActive(user));
        }

        [Fact]
        public void IsLicenseActive_WhenExpired_ReturnsFalse()
        {
            var user = new User { LicenseExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) };

            Assert.False(_sut.IsLicenseActive(user));
        }

        [Fact]
        public void IsLicenseActive_WhenNullUser_ReturnsFalse()
        {
            Assert.False(_sut.IsLicenseActive(null!));
        }
    }
}
