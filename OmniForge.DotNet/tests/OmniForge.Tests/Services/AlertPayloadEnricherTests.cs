using Moq;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Services;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace OmniForge.Tests.Services;

public class AlertPayloadEnricherTests
{
    private readonly Mock<IAlertRepository> _mockAlertRepository;
    private readonly AlertPayloadEnricher _enricher;

    public AlertPayloadEnricherTests()
    {
        _mockAlertRepository = new Mock<IAlertRepository>();
        var mockLogger = new Mock<ILogger<AlertPayloadEnricher>>();
        _mockAlertRepository.Setup(x => x.GetAlertsAsync(It.IsAny<string>())).ReturnsAsync(new List<Alert>());
        _enricher = new AlertPayloadEnricher(_mockAlertRepository.Object, mockLogger.Object);
    }

    [Fact]
    public async Task EnrichPayloadAsync_NoMatchingAlert_UnknownType_ReturnsBaseData()
    {
        var baseData = new { displayName = "TestUser" };
        var result = await _enricher.EnrichPayloadAsync("user1", "unknownAlertType", baseData);
        Assert.Same(baseData, result);
    }

    [Fact]
    public async Task EnrichPayloadAsync_NoUserAlert_FallsBackToDefaultTemplate()
    {
        // No user-configured alerts in the repository (constructor sets up empty list).
        // For a known alert type like "follow", the enricher should fall back to the default template.
        var baseData = new { displayName = "TestUser" };
        var result = await _enricher.EnrichPayloadAsync("user1", "follow", baseData);

        Assert.NotSame(baseData, result);
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("New Follower", json); // Default template name
        Assert.Contains("doorCreak.wav", json); // Default sound trigger
    }

    [Fact]
    public async Task EnrichPayloadAsync_DisabledAlert_ReturnsSuppressed()
    {
        _mockAlertRepository.Setup(x => x.GetAlertsAsync("user1")).ReturnsAsync(new List<Alert>
        {
            new Alert { Type = "follow", IsEnabled = false }
        });

        var result = await _enricher.EnrichPayloadAsync("user1", "follow", new { });
        Assert.True(_enricher.IsSuppressed(result));
    }

    [Fact]
    public async Task EnrichPayloadAsync_EnabledAlert_EnrichesPayload()
    {
        _mockAlertRepository.Setup(x => x.GetAlertsAsync("user1")).ReturnsAsync(new List<Alert>
        {
            new Alert
            {
                Id = "a1",
                Type = "follow",
                Name = "Follow Alert",
                VisualCue = "img.png",
                Sound = "doorCreak.wav",
                SoundDescription = "Door creak",
                TextPrompt = "Welcome [User]!",
                Duration = 5000,
                BackgroundColor = "#111",
                TextColor = "#fff",
                BorderColor = "#333",
                Effects = "{}",
                IsEnabled = true
            }
        });

        var baseData = new { displayName = "Alice", textPrompt = "New Follower: Alice" };
        var result = await _enricher.EnrichPayloadAsync("user1", "follow", baseData);

        Assert.False(_enricher.IsSuppressed(result));

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("Welcome Alice!", json);
        Assert.Contains("doorCreak.wav", json);
        Assert.Contains("a1", json);
    }

    [Fact]
    public async Task EnrichPayloadAsync_MergesEventDataWithoutOverwritingAlertFields()
    {
        _mockAlertRepository.Setup(x => x.GetAlertsAsync("user1")).ReturnsAsync(new List<Alert>
        {
            new Alert
            {
                Id = "a1",
                Type = "subscription",
                Name = "Sub Alert",
                TextPrompt = "[User] subscribed!",
                Duration = 4000,
                BackgroundColor = "#000",
                TextColor = "#fff",
                BorderColor = "#666",
                Effects = "{}",
                IsEnabled = true
            }
        });

        var baseData = new { displayName = "Bob", tier = "Tier 1", extraField = "extraValue" };
        var result = await _enricher.EnrichPayloadAsync("user1", "subscription", baseData);

        var json = JsonSerializer.Serialize(result);
        // Alert fields preserved
        Assert.Contains("Sub Alert", json);
        // Event data merged
        Assert.Contains("extraValue", json);
        // Template substitution happened
        Assert.Contains("Bob subscribed!", json);
    }

    [Fact]
    public void IsSuppressed_SuppressedPayload_ReturnsTrue()
    {
        var payload = new Dictionary<string, object> { ["suppress"] = true };
        Assert.True(_enricher.IsSuppressed(payload));
    }

    [Fact]
    public void IsSuppressed_NormalPayload_ReturnsFalse()
    {
        Assert.False(_enricher.IsSuppressed(new { data = "test" }));
    }

    [Fact]
    public void ApplyTemplate_SubstitutesAllTokens()
    {
        var payload = new Dictionary<string, object>
        {
            ["displayName"] = "Alice",
            ["tier"] = "Tier 3",
            ["months"] = 12,
            ["amount"] = 500,
            ["viewers"] = 100,
            ["recipientName"] = "Bob",
            ["message"] = "Hello!"
        };

        var result = AlertPayloadEnricher.ApplyTemplate(
            "[User] gave [Amount] bits, [Tier] sub, [Months] months, [Viewers] viewers, [Recipient], [Message]",
            payload);

        Assert.Contains("Alice", result);
        Assert.Contains("500", result);
        Assert.Contains("Tier 3", result);
        Assert.Contains("12", result);
        Assert.Contains("100", result);
        Assert.Contains("Bob", result);
        Assert.Contains("Hello!", result);
    }

    [Fact]
    public void ApplyTemplate_UnknownTokensPreserved()
    {
        var payload = new Dictionary<string, object>();
        var result = AlertPayloadEnricher.ApplyTemplate("Hello [Unknown]!", payload);
        Assert.Equal("Hello [Unknown]!", result);
    }

    [Fact]
    public async Task EnrichPayloadAsync_InvalidEffectsJson_StillEnrichesPayload()
    {
        _mockAlertRepository.Setup(x => x.GetAlertsAsync("user1")).ReturnsAsync(new List<Alert>
        {
            new Alert
            {
                Id = "a1",
                Type = "follow",
                Name = "Follow Alert",
                TextPrompt = "Welcome!",
                Duration = 5000,
                BackgroundColor = "#000",
                TextColor = "#fff",
                BorderColor = "#333",
                Effects = "not-valid-json{{",
                IsEnabled = true
            }
        });

        var result = await _enricher.EnrichPayloadAsync("user1", "follow", new { displayName = "Alice" });

        // Should still enrich — invalid Effects JSON is skipped silently
        Assert.False(_enricher.IsSuppressed(result));
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("Follow Alert", json);
        // Effects key should be absent since parsing failed
        Assert.DoesNotContain("\"effects\"", json);
    }

    [Fact]
    public async Task EnrichPayloadAsync_RepositoryThrows_ReturnBaseData()
    {
        var baseData = new { displayName = "Alice" };
        _mockAlertRepository.Setup(x => x.GetAlertsAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var result = await _enricher.EnrichPayloadAsync("user1", "follow", baseData);

        // Should fall back to passthrough without throwing
        Assert.Same(baseData, result);
    }
}
