using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Components.Settings;
using Xunit;

namespace OmniForge.Tests.Components.Pages.Settings;

public class GameLibraryManagerTests : BunitContext
{
    private readonly Mock<IGameLibraryRepository> _mockGameLibraryRepository;
    private readonly Mock<IGameCountersRepository> _mockGameCountersRepository;
    private readonly Mock<IGameContextRepository> _mockGameContextRepository;
    private readonly Mock<IGameSwitchService> _mockGameSwitchService;
    private readonly Mock<ITwitchApiService> _mockTwitchApiService;
    private readonly Mock<ICounterLibraryRepository> _mockCounterLibraryRepository;
    private readonly Mock<ICounterRequestRepository> _mockCounterRequestRepository;
    private readonly Mock<IGameCounterSetupService> _mockGameCounterSetupService;
    private readonly Mock<IGameCoreCountersConfigRepository> _mockGameCoreCountersConfigRepository;
    private readonly Mock<IUserRepository> _mockUserRepository;

    public GameLibraryManagerTests()
    {
        _mockGameLibraryRepository = new Mock<IGameLibraryRepository>();
        _mockGameCountersRepository = new Mock<IGameCountersRepository>();
        _mockGameContextRepository = new Mock<IGameContextRepository>();
        _mockGameSwitchService = new Mock<IGameSwitchService>();
        _mockTwitchApiService = new Mock<ITwitchApiService>();
        _mockCounterLibraryRepository = new Mock<ICounterLibraryRepository>();
        _mockCounterRequestRepository = new Mock<ICounterRequestRepository>();
        _mockGameCounterSetupService = new Mock<IGameCounterSetupService>();
        _mockGameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>();
        _mockUserRepository = new Mock<IUserRepository>();

        Services.AddSingleton(_mockGameLibraryRepository.Object);
        Services.AddSingleton(_mockGameCountersRepository.Object);
        Services.AddSingleton(_mockGameContextRepository.Object);
        Services.AddSingleton(_mockGameSwitchService.Object);
        Services.AddSingleton(_mockTwitchApiService.Object);
        Services.AddSingleton(_mockCounterLibraryRepository.Object);
        Services.AddSingleton(_mockCounterRequestRepository.Object);
        Services.AddSingleton(_mockGameCounterSetupService.Object);
        Services.AddSingleton(_mockGameCoreCountersConfigRepository.Object);
        Services.AddSingleton(_mockUserRepository.Object);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EnabledCounters_ShouldShowCoreAndOnlyPerGameCustomCounters()
    {
        var userId = "user1";
        var gameId = "game1";

        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(new User { TwitchUserId = userId });

        _mockGameLibraryRepository.Setup(r => r.ListAsync(userId, It.IsAny<int>())).ReturnsAsync(new[]
        {
            new GameLibraryItem
            {
                UserId = userId,
                GameId = gameId,
                GameName = "Test Game",
                BoxArtUrl = "",
                LastSeenAt = DateTimeOffset.UtcNow
            }
        });

        _mockGameContextRepository.Setup(r => r.GetAsync(userId)).ReturnsAsync(new GameContext { UserId = userId, ActiveGameId = gameId });

        // Library includes core + non-core. Core should be filtered from the modal.
        _mockCounterLibraryRepository.Setup(r => r.ListAsync()).ReturnsAsync(new[]
        {
            new CounterLibraryItem { CounterId = "deaths", Name = "Deaths" },
            new CounterLibraryItem { CounterId = "wins", Name = "Wins" },
            new CounterLibraryItem { CounterId = "losses", Name = "Losses" }
        });

        _mockGameCountersRepository.Setup(r => r.GetAsync(userId, gameId)).ReturnsAsync(new Counter
        {
            TwitchUserId = userId,
            Deaths = 1,
            Swears = 2,
            Screams = 3,
            Bits = 4,
            CustomCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["wins"] = 7
            },
            LastUpdated = DateTimeOffset.UtcNow
        });

        _mockGameCoreCountersConfigRepository
            .Setup(r => r.GetAsync(userId, gameId))
            .ReturnsAsync(new GameCoreCountersConfig(userId, gameId, true, true, true, false, DateTimeOffset.UtcNow));

        var cut = Render(b =>
        {
            b.OpenComponent<GameLibraryManager>(0);
            b.AddAttribute(1, nameof(GameLibraryManager.UserId), userId);
            b.CloseComponent();
        });

        // Wait for initial load
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));

        // Select game
        cut.FindAll("button.list-group-item").Single(b => b.TextContent.Contains("Test Game")).Click();

        // Enabled Counters should include Wins, but not Losses (not added to this game)
        cut.WaitForState(() => cut.Markup.Contains("Enabled Counters"));

        Assert.Contains("Deaths", cut.Markup);
        Assert.Contains("Swears", cut.Markup);
        Assert.Contains("Screams", cut.Markup);
        Assert.Contains("Bits", cut.Markup);

        Assert.Contains("Wins", cut.Markup);
        Assert.DoesNotContain("Losses", cut.Markup);
    }

    [Fact]
    public void CounterLibraryModal_ShouldNotListCoreCounters()
    {
        var userId = "user1";
        var gameId = "game1";

        _mockUserRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(new User { TwitchUserId = userId });

        _mockGameLibraryRepository.Setup(r => r.ListAsync(userId, It.IsAny<int>())).ReturnsAsync(new[]
        {
            new GameLibraryItem
            {
                UserId = userId,
                GameId = gameId,
                GameName = "Test Game",
                BoxArtUrl = "",
                LastSeenAt = DateTimeOffset.UtcNow
            }
        });

        _mockGameContextRepository.Setup(r => r.GetAsync(userId)).ReturnsAsync(new GameContext { UserId = userId, ActiveGameId = gameId });

        _mockCounterLibraryRepository.Setup(r => r.ListAsync()).ReturnsAsync(new[]
        {
            new CounterLibraryItem { CounterId = "deaths", Name = "Deaths" },
            new CounterLibraryItem { CounterId = "wins", Name = "Wins" }
        });

        _mockGameCountersRepository.Setup(r => r.GetAsync(userId, gameId)).ReturnsAsync(new Counter
        {
            TwitchUserId = userId,
            CustomCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["wins"] = 1
            },
            LastUpdated = DateTimeOffset.UtcNow
        });

        _mockGameCoreCountersConfigRepository
            .Setup(r => r.GetAsync(userId, gameId))
            .ReturnsAsync(new GameCoreCountersConfig(userId, gameId, true, true, true, false, DateTimeOffset.UtcNow));

        var cut = Render(b =>
        {
            b.OpenComponent<GameLibraryManager>(0);
            b.AddAttribute(1, nameof(GameLibraryManager.UserId), userId);
            b.CloseComponent();
        });
        cut.WaitForState(() => !cut.Markup.Contains("spinner-border"));

        // Select game
        cut.FindAll("button.list-group-item").Single(b => b.TextContent.Contains("Test Game")).Click();
        cut.WaitForState(() => cut.Markup.Contains("Enabled Counters"));

        // Open modal
        cut.Find("button.btn.btn-sm.btn-outline-primary").Click();

        cut.WaitForState(() => cut.Markup.Contains("Add Counters"));

        // Core counter should not appear in modal list
        Assert.DoesNotContain("Key: deaths", cut.Markup);
        Assert.Contains("Key: wins", cut.Markup);

        // Already-added counter should show a Remove button
        var removeButtons = cut.FindAll(".modal-content button.btn-outline-danger");
        Assert.True(removeButtons.Any(b => b.TextContent.Contains("Remove")), "Expected a Remove button for already-added counter.");
    }
}
