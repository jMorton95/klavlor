using KlavLor.Application.Features.Loot.Feed;
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

            var tiers = await repository.GetAllFeedTiers(50);
            var totalSeeded = 0;

            foreach (var (_, entries) in tiers)
            {
                if (entries.Count > 0)
                {
                    var collapsed = LootFeedGrouping.CollapseAdjacent(entries);
                    feedService.SeedBuffer(collapsed);
                    totalSeeded += collapsed.Count;
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
