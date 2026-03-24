using System.Collections.Generic;
using OmniForge.Core.Utilities;
using Xunit;

namespace OmniForge.Tests
{
    public class LogValueTests
    {
        [Fact]
        public void Safe_String_EscapesNewlines()
        {
            Assert.Equal("hello\\nworld\\r", LogValue.Safe("hello\nworld\r"));
        }

        [Fact]
        public void Safe_NullString_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, LogValue.Safe((string?)null));
        }

        [Fact]
        public void Safe_Object_CallsToStringAndEscapes()
        {
            Assert.Equal("42", LogValue.Safe((object?)42));
        }

        [Fact]
        public void Safe_NullObject_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, LogValue.Safe((object?)null));
        }

        [Fact]
        public void JoinSafe_CombinesValues()
        {
            Assert.Equal("a, b, c", LogValue.JoinSafe(new[] { "a", "b", "c" }));
        }

        [Fact]
        public void JoinSafe_HandlesNullValues()
        {
            Assert.Equal("a, , c", LogValue.JoinSafe(new[] { "a", null, "c" }));
        }

        [Fact]
        public void JoinSafe_NullCollection_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, LogValue.JoinSafe(null));
        }

        [Fact]
        public void JoinSafe_CustomSeparator()
        {
            Assert.Equal("a|b", LogValue.JoinSafe(new[] { "a", "b" }, "|"));
        }
    }
}
