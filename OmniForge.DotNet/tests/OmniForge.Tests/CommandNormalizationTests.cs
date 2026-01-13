using OmniForge.Infrastructure.Utilities;
using Xunit;

namespace OmniForge.Tests
{
    public class CommandNormalizationTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void NormalizeBaseCommandOrEmpty_ShouldReturnEmpty_ForNullOrWhitespace(string? input, string expected)
        {
            var result = CommandNormalization.NormalizeBaseCommandOrEmpty(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("p", "!p")]
        [InlineData("P", "!p")]
        [InlineData("!P", "!p")]
        [InlineData("  !Pulls  ", "!pulls")]
        public void NormalizeBaseCommandOrEmpty_ShouldNormalizePrefixAndCase(string input, string expected)
        {
            var result = CommandNormalization.NormalizeBaseCommandOrEmpty(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("!pulls+", "!pulls")]
        [InlineData("!pulls-", "!pulls")]
        [InlineData("pulls+", "!pulls")]
        [InlineData("pulls-", "!pulls")]
        public void NormalizeBaseCommandOrEmpty_ShouldTrimTrailingOperators(string input, string expected)
        {
            var result = CommandNormalization.NormalizeBaseCommandOrEmpty(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null, "pulls", "!pulls")]
        [InlineData(" ", "pulls", "!pulls")]
        [InlineData("!", "pulls", "!pulls")]
        [InlineData("!p", "pulls", "!p")]
        public void NormalizeBaseCommandOrDefault_ShouldUseFallbackWhenInvalid(string? input, string fallback, string expected)
        {
            var result = CommandNormalization.NormalizeBaseCommandOrDefault(input, fallback);
            Assert.Equal(expected, result);
        }
    }
}
