using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Repositories;
using Xunit;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Moq;
using OmniForge.Core.Configuration;

namespace OmniForge.Tests
{
    public class TemplateRepositoryTests
    {
        [Fact]
        public async Task GetAvailableTemplatesAsync_ShouldReturnAllBuiltInTemplates()
        {
            // Arrange - Create mock for constructor dependencies
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();

            // Assert
            Assert.NotNull(templates);
            Assert.Equal(3, templates.Count);
            Assert.Contains("asylum_themed", templates.Keys);
            Assert.Contains("modern_minimal", templates.Keys);
            Assert.Contains("streamer_pro", templates.Keys);
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_AsylumThemed_ShouldHaveCorrectProperties()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var asylumTemplate = templates["asylum_themed"];

            // Assert
            Assert.Equal("Asylum Themed", asylumTemplate.Name);
            Assert.Equal("built-in", asylumTemplate.Type);
            Assert.Contains("horror", asylumTemplate.Description.ToLower());
            Assert.NotNull(asylumTemplate.Config);
            Assert.NotNull(asylumTemplate.Config.Colors);
            Assert.NotNull(asylumTemplate.Config.Fonts);
            Assert.NotNull(asylumTemplate.Config.Animations);
            Assert.NotNull(asylumTemplate.Config.Sounds);
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_AsylumThemed_ShouldHaveCorrectColors()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var colors = templates["asylum_themed"].Config.Colors;

            // Assert
            Assert.Equal("#8B0000", colors.Primary);
            Assert.Equal("#DC143C", colors.Secondary);
            Assert.Equal("#1a0000", colors.Background);
            Assert.Equal("#FFFFFF", colors.Text);
            Assert.Equal("#FF6B6B", colors.Accent);
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_AsylumThemed_ShouldHaveAnimationsEnabled()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var animations = templates["asylum_themed"].Config.Animations;

            // Assert
            Assert.True(animations.BloodDrip);
            Assert.True(animations.Screenshake);
            Assert.True(animations.FadeEffects);
            Assert.True(animations.ParticleEffects);
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_ModernMinimal_ShouldHaveCorrectProperties()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var modernTemplate = templates["modern_minimal"];

            // Assert
            Assert.Equal("Modern Minimal", modernTemplate.Name);
            Assert.Equal("built-in", modernTemplate.Type);
            Assert.Contains("clean", modernTemplate.Description.ToLower());
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_ModernMinimal_ShouldHaveCorrectColors()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var colors = templates["modern_minimal"].Config.Colors;

            // Assert
            Assert.Equal("#6366f1", colors.Primary);
            Assert.Equal("#8b5cf6", colors.Secondary);
            Assert.Equal("#ffffff", colors.Background);
            Assert.Equal("#1f2937", colors.Text);
            Assert.Equal("#3b82f6", colors.Accent);
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_ModernMinimal_ShouldHaveGlassmorphism()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var animations = templates["modern_minimal"].Config.Animations;

            // Assert
            Assert.True(animations.Glassmorphism);
            Assert.True(animations.SlideIn);
            Assert.True(animations.FadeEffects);
            Assert.True(animations.BounceOnUpdate);
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_StreamerPro_ShouldHaveCorrectProperties()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var streamerTemplate = templates["streamer_pro"];

            // Assert
            Assert.Equal("Streamer Pro", streamerTemplate.Name);
            Assert.Equal("built-in", streamerTemplate.Type);
            Assert.Contains("professional", streamerTemplate.Description.ToLower());
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_StreamerPro_ShouldHaveTwitchColors()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var colors = templates["streamer_pro"].Config.Colors;

            // Assert
            Assert.Equal("#9146ff", colors.Primary); // Twitch purple
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_StreamerPro_ShouldHaveNeonGlow()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();
            var animations = templates["streamer_pro"].Config.Animations;

            // Assert
            Assert.True(animations.NeonGlow);
            Assert.True(animations.Typewriter);
            Assert.True(animations.ParticleTrails);
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_AllTemplates_ShouldHaveSounds()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();

            // Assert
            foreach (var template in templates.Values)
            {
                Assert.NotNull(template.Config.Sounds);
                Assert.False(string.IsNullOrEmpty(template.Config.Sounds.Death));
                Assert.False(string.IsNullOrEmpty(template.Config.Sounds.Swear));
                Assert.False(string.IsNullOrEmpty(template.Config.Sounds.Milestone));
            }
        }

        [Fact]
        public async Task GetAvailableTemplatesAsync_AllTemplates_ShouldHaveFonts()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var templates = await repository.GetAvailableTemplatesAsync();

            // Assert
            foreach (var template in templates.Values)
            {
                Assert.NotNull(template.Config.Fonts);
                Assert.False(string.IsNullOrEmpty(template.Config.Fonts.Primary));
                Assert.False(string.IsNullOrEmpty(template.Config.Fonts.Secondary));
            }
        }

        #region InitializeAsync Tests

        [Fact]
        public async Task InitializeAsync_ShouldCreateTableIfNotExists()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            await repository.InitializeAsync();

            // Assert
            mockTableClient.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region GetUserCustomTemplateAsync Tests

        [Fact]
        public async Task GetUserCustomTemplateAsync_ShouldReturnNull_WhenNotFound()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);
            mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not Found"));

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var result = await repository.GetUserCustomTemplateAsync("user123");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetUserCustomTemplateAsync_ShouldReturnTemplate_WhenFound()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var template = new Template
            {
                Name = "Custom Template",
                Description = "My custom template",
                Type = "custom",
                Config = new TemplateConfig
                {
                    Colors = new TemplateColors { Primary = "#FF0000" }
                }
            };
            var templateJson = System.Text.Json.JsonSerializer.Serialize(template);

            var tableEntity = new TableEntity("user123", "customTemplate")
            {
                ["templateConfig"] = templateJson
            };

            var mockResponse = new Mock<Response<TableEntity>>();
            mockResponse.Setup(x => x.Value).Returns(tableEntity);

            mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>("user123", "customTemplate", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var result = await repository.GetUserCustomTemplateAsync("user123");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Custom Template", result.Name);
            Assert.Equal("custom", result.Type);
        }

        [Fact]
        public async Task GetUserCustomTemplateAsync_ShouldReturnNull_WhenTemplateConfigMissing()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var tableEntity = new TableEntity("user123", "customTemplate");
            // No templateConfig property

            var mockResponse = new Mock<Response<TableEntity>>();
            mockResponse.Setup(x => x.Value).Returns(tableEntity);

            mockTableClient.Setup(x => x.GetEntityAsync<TableEntity>("user123", "customTemplate", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResponse.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            // Act
            var result = await repository.GetUserCustomTemplateAsync("user123");

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region SaveUserCustomTemplateAsync Tests

        [Fact]
        public async Task SaveUserCustomTemplateAsync_ShouldUpsertEntity()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            var template = new Template
            {
                Name = "My Custom Template",
                Description = "A custom template",
                Type = "custom"
            };

            // Act
            await repository.SaveUserCustomTemplateAsync("user123", template);

            // Assert
            mockTableClient.Verify(x => x.UpsertEntityAsync(
                It.Is<TableEntity>(e => e.PartitionKey == "user123" && e.RowKey == "customTemplate"),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveUserCustomTemplateAsync_ShouldSerializeTemplateToJson()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            TableEntity? capturedEntity = null;
            mockTableClient.Setup(x => x.UpsertEntityAsync(It.IsAny<TableEntity>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
                .Callback<TableEntity, TableUpdateMode, CancellationToken>((e, m, c) => capturedEntity = e);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            var template = new Template
            {
                Name = "Test Template",
                Description = "Test description",
                Type = "custom",
                Config = new TemplateConfig
                {
                    Colors = new TemplateColors { Primary = "#123456" }
                }
            };

            // Act
            await repository.SaveUserCustomTemplateAsync("user123", template);

            // Assert
            Assert.NotNull(capturedEntity);
            Assert.True(capturedEntity.ContainsKey("templateConfig"));
            var templateConfigJson = capturedEntity["templateConfig"] as string;
            Assert.NotNull(templateConfigJson);
            Assert.Contains("Test Template", templateConfigJson);
            Assert.Contains("#123456", templateConfigJson);
        }

        [Fact]
        public async Task SaveUserCustomTemplateAsync_ShouldIncludeLastUpdatedTimestamp()
        {
            // Arrange
            var mockTableServiceClient = new Mock<TableServiceClient>();
            var mockTableClient = new Mock<TableClient>();
            mockTableServiceClient.Setup(x => x.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);

            TableEntity? capturedEntity = null;
            mockTableClient.Setup(x => x.UpsertEntityAsync(It.IsAny<TableEntity>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
                .Callback<TableEntity, TableUpdateMode, CancellationToken>((e, m, c) => capturedEntity = e);

            var options = Options.Create(new AzureTableConfiguration { UsersTable = "users" });
            var repository = new TemplateRepository(mockTableServiceClient.Object, options);

            var template = new Template { Name = "Test" };

            // Act
            await repository.SaveUserCustomTemplateAsync("user123", template);

            // Assert
            Assert.NotNull(capturedEntity);
            Assert.True(capturedEntity.ContainsKey("lastUpdated"));
            var lastUpdated = (DateTimeOffset)capturedEntity["lastUpdated"]!;
            Assert.True(lastUpdated <= DateTimeOffset.UtcNow);
            Assert.True(lastUpdated > DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        #endregion
    }
}
