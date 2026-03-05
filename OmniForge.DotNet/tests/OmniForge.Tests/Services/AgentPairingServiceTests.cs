using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class AgentPairingServiceTests
    {
        [Fact]
        public void TryRegisterCode_SucceedsForNewCode()
        {
            var svc = new AgentPairingService();
            var result = svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.True(result);
        }

        [Fact]
        public void TryRegisterCode_FailsForDuplicateCode()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(5));
            var result = svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.False(result);
        }

        [Fact]
        public void TryApprove_SucceedsForRegisteredCode()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(5));
            var result = svc.TryApprove("ABC123", "user1", "jwt-token");
            Assert.True(result);
        }

        [Fact]
        public void TryApprove_FailsForUnknownCode()
        {
            var svc = new AgentPairingService();
            var result = svc.TryApprove("UNKNOWN", "user1", "jwt-token");
            Assert.False(result);
        }

        [Fact]
        public void TryApprove_FailsForExpiredCode()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(-1));
            var result = svc.TryApprove("ABC123", "user1", "jwt-token");
            Assert.False(result);
        }

        [Fact]
        public void TryPoll_ReturnsPendingForRegisteredUnapprovedCode()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(5));
            var entry = svc.TryPoll("ABC123");
            Assert.NotNull(entry);
            Assert.False(entry.IsApproved);
            Assert.False(entry.IsExpired);
        }

        [Fact]
        public void TryPoll_ReturnsApprovedEntry()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(5));
            svc.TryApprove("ABC123", "user1", "jwt-token");
            var entry = svc.TryPoll("ABC123");
            Assert.NotNull(entry);
            Assert.True(entry.IsApproved);
            Assert.Equal("jwt-token", entry.Token);
        }

        [Fact]
        public void TryPoll_ReturnsExpiredForExpiredCode()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(-1));
            var entry = svc.TryPoll("ABC123");
            Assert.NotNull(entry);
            Assert.True(entry.IsExpired);
        }

        [Fact]
        public void TryPoll_ReturnsNullForUnknownCode()
        {
            var svc = new AgentPairingService();
            var entry = svc.TryPoll("UNKNOWN");
            Assert.Null(entry);
        }

        [Fact]
        public void TryPoll_RemovesApprovedEntryAfterPoll()
        {
            var svc = new AgentPairingService();
            svc.TryRegisterCode("ABC123", DateTimeOffset.UtcNow.AddMinutes(5));
            svc.TryApprove("ABC123", "user1", "jwt-token");

            // First poll returns approved
            var entry = svc.TryPoll("ABC123");
            Assert.NotNull(entry);
            Assert.True(entry.IsApproved);

            // Second poll returns null (entry removed)
            var entry2 = svc.TryPoll("ABC123");
            Assert.Null(entry2);
        }
    }
}
