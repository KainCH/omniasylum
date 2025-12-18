using System;
using OmniForge.Web;
using Xunit;

namespace OmniForge.Tests
{
    public class ServerInstanceTests
    {
        [Fact]
        public void Id_ShouldBeEightCharacters()
        {
            // The ServerInstance.Id should be 8 characters (first 8 chars of GUID without hyphens)
            Assert.Equal(8, ServerInstance.Id.Length);
        }

        [Fact]
        public void Id_ShouldContainOnlyHexadecimalCharacters()
        {
            // GUID.ToString("N") produces lowercase hex characters
            foreach (var c in ServerInstance.Id)
            {
                Assert.True(char.IsLetterOrDigit(c), $"Character '{c}' is not alphanumeric");
                Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
                    $"Character '{c}' is not a valid hexadecimal character");
            }
        }

        [Fact]
        public void Id_ShouldBeConsistent_AcrossMultipleReads()
        {
            // Static readonly should return same value
            var first = ServerInstance.Id;
            var second = ServerInstance.Id;
            var third = ServerInstance.Id;

            Assert.Equal(first, second);
            Assert.Equal(second, third);
        }

        [Fact]
        public void StartTime_ShouldBeSet()
        {
            // StartTime should be initialized
            Assert.NotEqual(default, ServerInstance.StartTime);
        }

        [Fact]
        public void StartTime_ShouldBeInThePast()
        {
            // StartTime should be at or before the current time
            Assert.True(ServerInstance.StartTime <= DateTimeOffset.UtcNow,
                "StartTime should be at or before the current time");
        }

        [Fact]
        public void StartTime_ShouldBeConsistent_AcrossMultipleReads()
        {
            // Static readonly should return same value
            var first = ServerInstance.StartTime;
            var second = ServerInstance.StartTime;

            Assert.Equal(first, second);
        }

        [Fact]
        public void StartTime_ShouldBeReasonablyRecent()
        {
            // StartTime should be before current time and after some reasonable past point
            // We check it's not in the future and was set within the application lifecycle
            Assert.True(ServerInstance.StartTime <= DateTimeOffset.UtcNow,
                "StartTime should not be in the future");
            Assert.True(ServerInstance.StartTime > DateTimeOffset.MinValue,
                "StartTime should be set to a valid value");
        }
    }
}
