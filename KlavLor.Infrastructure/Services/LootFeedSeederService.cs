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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ILootLogRepository>();

            // Seed each scope independently so the main and leagues feeds both have
            // history available immediately after restart.
            foreach (var feedScope in Enum.GetValues<LootFeedScope>())
            {
                var tiers = await repository.GetAllFeedTiers(50, feedScope);
                var seededForScope = 0;

                foreach (var (_, entries) in tiers)
                {
                    if (entries.Count > 0)
                    {
                        feedService.SeedBuffer(feedScope, entries);
                        seededForScope += entries.Count;
                    }
                }

                if (seededForScope > 0)
                    logger.LogInformation("Seeded {Scope} loot feed with {Count} entries", feedScope, seededForScope);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed loot feed buffer");
        }
    }
}
