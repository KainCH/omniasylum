using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services;

public sealed class CoreCounterLibrarySeeder
{
    private readonly ICounterLibraryRepository _counterLibraryRepository;

    public CoreCounterLibrarySeeder(ICounterLibraryRepository counterLibraryRepository)
    {
        _counterLibraryRepository = counterLibraryRepository;
    }

    public async Task SeedAsync()
    {
        // Ids are intentionally stable because they are used as keys in chat commands/variables.
        var now = DateTimeOffset.UtcNow;

        await _counterLibraryRepository.UpsertAsync(new CounterLibraryItem
        {
            CounterId = "deaths",
            Name = "Deaths",
            Icon = "bi-skull",
            CreatedAt = now,
            LastUpdated = now
        });

        await _counterLibraryRepository.UpsertAsync(new CounterLibraryItem
        {
            CounterId = "swears",
            Name = "Swears",
            Icon = "bi-chat-dots",
            CreatedAt = now,
            LastUpdated = now
        });

        await _counterLibraryRepository.UpsertAsync(new CounterLibraryItem
        {
            CounterId = "screams",
            Name = "Screams",
            Icon = "bi-volume-up",
            CreatedAt = now,
            LastUpdated = now
        });

        await _counterLibraryRepository.UpsertAsync(new CounterLibraryItem
        {
            CounterId = "bits",
            Name = "Bits",
            Icon = "bi-gem",
            CreatedAt = now,
            LastUpdated = now
        });
    }
}
