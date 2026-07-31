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
    IJobRunRecorder jobRuns,
    ILogger<LootFeedSeederService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await jobRuns.Track("Loot feed seeder", RunOnce);
    }

    private async Task<JobRunResult> RunOnce()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            // Goes through the handler, not straight to the repository, so seeded entries carry the
            // per-drop effective rates. Buffered entries are merged with live drops on publish, and
            // a merged card would otherwise show rate chips only on the freshly arrived items.
            var feedTiers = scope.ServiceProvider.GetRequiredService<LootFeedTiersHandler>();

            var totalSeeded = 0;

            // Seed each scope independently so the main and leagues feeds both have
            // history available immediately after restart.
            foreach (var feedScope in Enum.GetValues<LootFeedScope>())
            {
                var tiers = await feedTiers.Handle(feedScope);
                var seededForScope = 0;

                foreach (var (_, entries) in tiers)
                {
                    if (entries.Count > 0)
                    {
                        feedService.SeedBuffer(feedScope, entries);
                        seededForScope += entries.Count;
                    }
                }

                totalSeeded += seededForScope;
                if (seededForScope > 0)
                    logger.LogInformation("Seeded {Scope} loot feed with {Count} entries", feedScope, seededForScope);
            }

            return totalSeeded > 0
                ? JobRunResult.Ok(totalSeeded, "feed buffers seeded from loot history")
                : JobRunResult.NoWork;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed loot feed buffer");
            return JobRunResult.Failed(ex.Message);
        }
    }
}
