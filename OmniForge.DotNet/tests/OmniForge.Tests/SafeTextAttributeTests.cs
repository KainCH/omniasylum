using System.ComponentModel.DataAnnotations;
using OmniForge.Web.Validation;
using Xunit;

namespace OmniForge.Tests
{
    public class SafeTextAttributeTests
    {
        [Fact]
        public void IsValid_ShouldAllow_Null()
        {
            var attr = new SafeTextAttribute();
            var ctx = new ValidationContext(new object()) { DisplayName = "Field" };

            var result = attr.GetValidationResult(null, ctx);

            Assert.Equal(ValidationResult.Success, result);
        }

        [Fact]
        public void IsValid_ShouldAllow_EmptyString()
        {
            var attr = new SafeTextAttribute();
            var ctx = new ValidationContext(new object()) { DisplayName = "Field" };

            var result = attr.GetValidationResult(string.Empty, ctx);

            Assert.Equal(ValidationResult.Success, result);
        }

        [Fact]
        public void IsValid_ShouldAllow_TabsAndNewlines()
        {
            var attr = new SafeTextAttribute();
            var ctx = new ValidationContext(new object()) { DisplayName = "Field" };

            var text = "Line1\nLine2\r\nLine3\tTabbed";
            var result = attr.GetValidationResult(text, ctx);

            Assert.Equal(ValidationResult.Success, result);
        }

        [Fact]
        public void IsValid_ShouldReject_NullCharacter()
        {
            var attr = new SafeTextAttribute();
            var ctx = new ValidationContext(new object()) { DisplayName = "Field" };

            var result = attr.GetValidationResult("Bad\u0000Text", ctx);

            Assert.NotEqual(ValidationResult.Success, result);
        }

        [Theory]
        [InlineData("\u202A")] // LRE
        [InlineData("\u202B")] // RLE
        [InlineData("\u202D")] // LRO
        [InlineData("\u202E")] // RLO
        [InlineData("\u202C")] // PDF
        [InlineData("\u2066")] // LRI
        [InlineData("\u2067")] // RLI
        [InlineData("\u2068")] // FSI
        [InlineData("\u2069")] // PDI
        public void IsValid_ShouldReject_BidiControls(string control)
        {
            var attr = new SafeTextAttribute();
            var ctx = new ValidationContext(new object()) { DisplayName = "Field" };

            var value = $"before {control} after";
            var result = attr.GetValidationResult(value, ctx);

            Assert.NotEqual(ValidationResult.Success, result);
        }

        [Fact]
        public void IsValid_ShouldReject_MixedValidInvalid()
        {
            var attr = new SafeTextAttribute();
            var ctx = new ValidationContext(new object()) { DisplayName = "Field" };

            var value = "ok\nthen-bad" + "\u202E" + "more";
            var result = attr.GetValidationResult(value, ctx);

            Assert.NotEqual(ValidationResult.Success, result);
        }
    }
}
