using OmniForge.Core.Utilities;
using Xunit;

namespace OmniForge.Tests
{
    public class LogSanitizerTests
    {
        [Fact]
        public void Sanitize_String_Null_ReturnsEmptyString()
        {
            var result = LogSanitizer.Sanitize((string?)null);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Sanitize_String_Empty_ReturnsEmptyString()
        {
            var result = LogSanitizer.Sanitize(string.Empty);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Sanitize_String_RemovesNewlinesAndCarriageReturns()
        {
            var result = LogSanitizer.Sanitize("a\r\nb\nc\rd");
            Assert.Equal("abcd", result);
        }

        [Fact]
        public void Sanitize_Object_Null_ReturnsNull()
        {
            var result = LogSanitizer.Sanitize((object?)null);
            Assert.Null(result);
        }

        [Fact]
        public void Sanitize_Object_ReturnsSanitizedString()
        {
            object input = "a\r\nb";
            var result = LogSanitizer.Sanitize(input);

            var asString = Assert.IsType<string>(result);
            Assert.Equal("ab", asString);
        }
    }
}
