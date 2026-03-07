using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class SceneActionServiceTests
    {
        private readonly Mock<ISceneActionRepository> _mockSceneActionRepo = new();
        private readonly Mock<IUserRepository> _mockUserRepo = new();
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier = new();
        private readonly Mock<ILogger<SceneActionService>> _mockLogger = new();
        private readonly OvertimeTrackerService _overtimeTracker;
        private readonly SceneActionService _service;

        public SceneActionServiceTests()
        {
            _overtimeTracker = new OvertimeTrackerService(
                _mockOverlayNotifier.Object,
                new Mock<ILogger<OvertimeTrackerService>>().Object);

            _service = new SceneActionService(
                _mockSceneActionRepo.Object,
                _mockUserRepo.Object,
                _mockOverlayNotifier.Object,
                _overtimeTracker,
                _mockLogger.Object);
        }

        [Fact]
        public async Task HandleSceneChanged_WhenNoAction_ShouldOnlyNotifySceneChange()
        {
            _mockSceneActionRepo.Setup(x => x.GetAsync("user1", "Gaming")).ReturnsAsync((SceneAction?)null);

            await _service.HandleSceneChangedAsync("user1", "Gaming", null);

            _mockOverlayNotifier.Verify(x => x.NotifySceneChangedAsync("user1", "Gaming"), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifySettingsUpdateAsync(It.IsAny<string>(), It.IsAny<OverlaySettings>()), Times.Never);
        }

        [Fact]
        public async Task HandleSceneChanged_WithCounterVisibility_ShouldPushModifiedSettings()
        {
            var action = new SceneAction
            {
                UserId = "user1",
                SceneName = "BRB",
                CounterVisibility = new Dictionary<string, string>
                {
                    { "Deaths", "hide" },
                    { "Swears", "hide" }
                }
            };
            _mockSceneActionRepo.Setup(x => x.GetAsync("user1", "BRB")).ReturnsAsync(action);
            _mockUserRepo.Setup(x => x.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                OverlaySettings = new OverlaySettings
                {
                    Counters = new OverlayCounters { Deaths = true, Swears = true, Screams = true }
                }
            });

            await _service.HandleSceneChangedAsync("user1", "BRB", "Gaming");

            _mockOverlayNotifier.Verify(x => x.NotifySettingsUpdateAsync("user1",
                It.Is<OverlaySettings>(s => s.Counters.Deaths == false && s.Counters.Swears == false && s.Counters.Screams == true)),
                Times.Once);
        }

        [Fact]
        public async Task HandleSceneChanged_WithAutoStartTimer_ShouldStartTimer()
        {
            var action = new SceneAction
            {
                UserId = "user1",
                SceneName = "Gaming",
                AutoStartTimer = true,
                TimerDurationMinutes = 60
            };
            _mockSceneActionRepo.Setup(x => x.GetAsync("user1", "Gaming")).ReturnsAsync(action);
            _mockUserRepo.Setup(x => x.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                OverlaySettings = new OverlaySettings()
            });

            await _service.HandleSceneChangedAsync("user1", "Gaming", null);

            _mockUserRepo.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.OverlaySettings.TimerManualRunning == true &&
                u.OverlaySettings.TimerDurationMinutes == 60 &&
                u.OverlaySettings.TimerManualStartUtc.HasValue)), Times.Once);
        }

        [Fact]
        public async Task HandleSceneChanged_WithOvertime_ShouldScheduleOvertime()
        {
            var action = new SceneAction
            {
                UserId = "user1",
                SceneName = "BRB",
                TimerEnabled = true,
                AutoStartTimer = true,
                TimerDurationMinutes = 5,
                Overtime = new OvertimeConfig { Enabled = true, Text = "OVERTIME!" }
            };
            _mockSceneActionRepo.Setup(x => x.GetAsync("user1", "BRB")).ReturnsAsync(action);
            _mockUserRepo.Setup(x => x.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                OverlaySettings = new OverlaySettings()
            });

            await _service.HandleSceneChangedAsync("user1", "BRB", null);

            Assert.True(_overtimeTracker.HasPendingOvertime("user1"));
        }

        [Fact]
        public async Task HandleSceneChanged_ShouldCancelPreviousOvertime()
        {
            // Schedule overtime for first scene
            var action1 = new SceneAction
            {
                UserId = "user1",
                SceneName = "BRB",
                TimerEnabled = true,
                AutoStartTimer = true,
                TimerDurationMinutes = 5,
                Overtime = new OvertimeConfig { Enabled = true }
            };
            _mockSceneActionRepo.Setup(x => x.GetAsync("user1", "BRB")).ReturnsAsync(action1);
            _mockSceneActionRepo.Setup(x => x.GetAsync("user1", "Gaming")).ReturnsAsync((SceneAction?)null);
            _mockUserRepo.Setup(x => x.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                OverlaySettings = new OverlaySettings()
            });

            await _service.HandleSceneChangedAsync("user1", "BRB", null);
            Assert.True(_overtimeTracker.HasPendingOvertime("user1"));

            // Switch to new scene — should cancel overtime
            await _service.HandleSceneChangedAsync("user1", "Gaming", "BRB");
            Assert.False(_overtimeTracker.HasPendingOvertime("user1"));
        }
    }
}
