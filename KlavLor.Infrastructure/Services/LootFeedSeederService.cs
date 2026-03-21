using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

public sealed class LootFeedSeederService(
    IServiceScopeFactory scopeFactory,
    ILootFeedService feedService,
    ILogger<LootFeedSeederService> logger) : BackgroundService
{
    private static readonly (long? Min, long? Max)[] TierRanges =
    [
        (10_000, 100_000),         // Standard: 10K – 100K
        (100_000, 1_000_000),      // Notable: 100K – 1M
        (1_000_000, 10_000_000),   // Epic: 1M – 10M
        (10_000_000, null)         // Legendary: 10M+
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ILootLogRepository>();

            var totalSeeded = 0;
            foreach (var (min, max) in TierRanges)
            {
                var entries = await repository.GetRecentFeedEntries(50, min, max);
                if (entries.Count > 0)
                {
                    feedService.SeedBuffer(entries);
                    totalSeeded += entries.Count;
                }
            }

            if (totalSeeded > 0)
                logger.LogInformation("Seeded loot feed with {Count} entries across all tiers", totalSeeded);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed loot feed buffer");
        }
    }
}
