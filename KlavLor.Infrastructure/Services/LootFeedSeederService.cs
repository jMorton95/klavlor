using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

public sealed class LootFeedSeederService(
    IServiceScopeFactory scopeFactory,
    ILootFeedService feedService,
    ILootRollFeed rollFeed,
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
            var feedRepository = scope.ServiceProvider.GetRequiredService<ILootFeedRepository>();
            var recordRepository = scope.ServiceProvider.GetRequiredService<ILootRecordRepository>();

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

                // Isolated: the ticker is the less important of the two buffers, and without this a
                // failure seeding it would abort the whole job through the outer catch - taking the
                // NEXT scope's feed seeding down with it. The swimlanes must not go unseeded because
                // the banner could not be.
                try
                {
                    totalSeeded += await SeedRollTicker(feedRepository, recordRepository, feedScope);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to seed the {Scope} roll ticker; the feed is unaffected", feedScope);
                }
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

    /// <summary>
    /// Fills the roll ticker's ring from the most recent kills, so the banner is populated on the
    /// first page load after a restart instead of blank until the clan's next kill.
    /// </summary>
    /// <remarks>
    /// Two reads, not one: the kills, then their roll numbers from GetKillOrdinals - the single
    /// place that rule lives. Only the records RuneLite gave no KillCount for need resolving, so on
    /// a roster of ordinary NPC kills the second read is usually tiny or skipped entirely.
    /// </remarks>
    private async Task<int> SeedRollTicker(
        ILootFeedRepository feedRepository, ILootRecordRepository recordRepository, LootFeedScope feedScope)
    {
        var rows = await feedRepository.GetRecentRolls(feedScope, RollSeedCount);
        if (rows.Count == 0) return 0;

        var needOrdinals = rows
            .Where(r => r.KillCount is not > 0)
            .Select(r => new KillOrdinalRequest(r.RecordId, r.GameCharacterId, r.SourceName, r.OccurredAt))
            .ToList();

        var ordinals = needOrdinals.Count > 0
            ? await recordRepository.GetKillOrdinals(needOrdinals)
            : [];

        // Oldest first: the banner prepends, so the last one seeded ends up leftmost, matching the
        // order live rolls arrive in. GetRecentRolls hands them back newest-first.
        var entries = rows
            .AsEnumerable()
            .Reverse()
            .Select(r => new LootRollEntry(
                r.CharacterName,
                r.GameCharacterId,
                r.SourceName,
                r.KillCount is > 0
                    ? r.KillCount
                    : ordinals.TryGetValue(r.RecordId, out var resolved) ? resolved : null,
                r.OccurredAt))
            .ToList();

        rollFeed.SeedBuffer(feedScope, entries);
        logger.LogInformation("Seeded {Scope} roll ticker with {Count} rolls", feedScope, entries.Count);
        return entries.Count;
    }

    /// <summary>
    /// Matches the ticker buffer's capacity, so a restart restores a full banner rather than a
    /// partial one that fills in as kills arrive.
    /// </summary>
    private const int RollSeedCount = 40;
}
