using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class OvertimeTrackerServiceTests
    {
        private readonly Mock<IOverlayNotifier> _mockNotifier = new();
        private readonly OvertimeTrackerService _tracker;

        public OvertimeTrackerServiceTests()
        {
            _tracker = new OvertimeTrackerService(
                _mockNotifier.Object,
                new Mock<ILogger<OvertimeTrackerService>>().Object);
        }

        [Fact]
        public void Schedule_ShouldTrackPendingOvertime()
        {
            var config = new OvertimeConfig { Enabled = true, Text = "OVERTIME!" };

            _tracker.Schedule("user1", "BRB", config, 5);

            Assert.True(_tracker.HasPendingOvertime("user1"));
        }

        [Fact]
        public void Cancel_ShouldRemovePendingOvertime()
        {
            var config = new OvertimeConfig { Enabled = true };

            _tracker.Schedule("user1", "BRB", config, 5);
            _tracker.Cancel("user1");

            Assert.False(_tracker.HasPendingOvertime("user1"));
        }

        [Fact]
        public void Cancel_WhenNoPending_ShouldNotThrow()
        {
            _tracker.Cancel("user1");
            Assert.False(_tracker.HasPendingOvertime("user1"));
        }

        [Fact]
        public void Schedule_ShouldCancelPrevious()
        {
            var config = new OvertimeConfig { Enabled = true };

            _tracker.Schedule("user1", "BRB", config, 5);
            _tracker.Schedule("user1", "Gaming", config, 10);

            // Should still have pending (the new one)
            Assert.True(_tracker.HasPendingOvertime("user1"));
        }

        [Fact]
        public void HasPendingOvertime_WhenNotScheduled_ReturnsFalse()
        {
            Assert.False(_tracker.HasPendingOvertime("nonexistent"));
        }
    }
}
