using System;
using OmniForge.Core.Entities;
using OmniForge.Core.Exceptions;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests.Core
{
    public class CoreDtoAndExceptionCoverageTests
    {
        [Fact]
        public void HelixSettings_DefaultsAndSetters_Work()
        {
            var reward = new HelixCustomReward();

            Assert.NotNull(reward.MaxPerStreamSetting);
            Assert.NotNull(reward.MaxPerUserPerStreamSetting);
            Assert.NotNull(reward.GlobalCooldownSetting);

            reward.MaxPerStreamSetting.IsEnabled = true;
            reward.MaxPerStreamSetting.MaxPerStream = 5;

            reward.MaxPerUserPerStreamSetting.IsEnabled = true;
            reward.MaxPerUserPerStreamSetting.MaxPerUserPerStream = 2;

            reward.GlobalCooldownSetting.IsEnabled = true;
            reward.GlobalCooldownSetting.GlobalCooldownSeconds = 30;

            Assert.True(reward.MaxPerStreamSetting.IsEnabled);
            Assert.Equal(5, reward.MaxPerStreamSetting.MaxPerStream);
            Assert.True(reward.MaxPerUserPerStreamSetting.IsEnabled);
            Assert.Equal(2, reward.MaxPerUserPerStreamSetting.MaxPerUserPerStream);
            Assert.True(reward.GlobalCooldownSetting.IsEnabled);
            Assert.Equal(30, reward.GlobalCooldownSetting.GlobalCooldownSeconds);
        }

        [Fact]
        public void ClipInfo_DefaultsAndSetters_Work()
        {
            var clip = new ClipInfo();
            Assert.Equal(string.Empty, clip.Id);
            Assert.Equal(string.Empty, clip.EditUrl);

            clip.Id = "clip1";
            clip.EditUrl = "https://clips.twitch.tv/edit";

            Assert.Equal("clip1", clip.Id);
            Assert.Equal("https://clips.twitch.tv/edit", clip.EditUrl);
        }

        [Fact]
        public void ConfigurationException_Constructors_Work()
        {
            var ex1 = new ConfigurationException("missing setting");
            Assert.Equal("missing setting", ex1.Message);

            var inner = new InvalidOperationException("boom");
            var ex2 = new ConfigurationException("wrapped", inner);
            Assert.Equal("wrapped", ex2.Message);
            Assert.Same(inner, ex2.InnerException);
        }

        [Fact]
        public void AddGameRequest_DefaultsAndSetters_Work()
        {
            var request = new AddGameRequest();
            Assert.Equal(string.Empty, request.GameId);
            Assert.Equal(string.Empty, request.GameName);
            Assert.Null(request.BoxArtUrl);

            request.GameId = "123";
            request.GameName = "Test";
            request.BoxArtUrl = " https://example/box.png ";

            Assert.Equal("123", request.GameId);
            Assert.Equal("Test", request.GameName);
            Assert.Equal(" https://example/box.png ", request.BoxArtUrl);
        }
    }
}
