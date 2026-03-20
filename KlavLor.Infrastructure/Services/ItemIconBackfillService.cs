using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

public sealed class ItemIconBackfillService(IServiceScopeFactory scopeFactory, ILogger<ItemIconBackfillService> logger) : BackgroundService
{
    private const int MaxFailAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app startup
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));

        try
        {
            // Run immediately on first tick, then every 10 minutes
            do
            {
                await RunCycle(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Item icon backfill service failed unexpectedly");
        }
    }

    private async Task RunCycle(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var itemIconRepo = scope.ServiceProvider.GetRequiredService<IItemIconRepository>();
            var wikiClient = scope.ServiceProvider.GetRequiredService<IOsrsWikiClient>();
            var imageCacheService = scope.ServiceProvider.GetRequiredService<IImageCacheService>();

            // Step 1: Discover new items from loot records
            var uncatalogued = await itemIconRepo.FindUncataloguedItems(50);
            if (uncatalogued.Count > 0)
            {
                logger.LogInformation("Item icon backfill: discovered {Count} new items", uncatalogued.Count);

                var newIcons = uncatalogued.Select(item => new ItemIcon
                {
                    ItemName = item.Name,
                    ItemId = item.ItemId
                }).ToList();

                await itemIconRepo.SaveRange(newIcons);
            }

            // Step 2: Resolve pending icons (no cached image yet, under fail threshold)
            var pending = await itemIconRepo.GetPendingIcons(20);
            if (pending.Count == 0)
            {
                if (uncatalogued.Count == 0)
                    logger.LogDebug("Item icon backfill: nothing to do");
                return;
            }

            logger.LogInformation("Item icon backfill: resolving {Count} pending icons", pending.Count);

            var consecutiveFetchFailures = 0;

            foreach (var icon in pending)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var results = await wikiClient.SearchItems(icon.ItemName, limit: 5);

                    // Pick best match: exact name match first, then first result
                    var match = results.FirstOrDefault(r => r.Name.Equals(icon.ItemName, StringComparison.OrdinalIgnoreCase))
                                ?? results.FirstOrDefault();

                    if (match?.IconUrl is not null)
                    {
                        var cached = await imageCacheService.GetOrCache(match.IconUrl);
                        if (cached is not null)
                        {
                            icon.CachedImageId = cached.Id;
                            consecutiveFetchFailures = 0;
                            logger.LogInformation("Cached icon for {ItemName} as image {ImageId}", icon.ItemName, cached.Id);
                        }
                        else
                        {
                            // Transient failure (rate limit, network blip) — don't increment FailCount
                            icon.LastAttemptAt = DateTimeOffset.UtcNow;
                            consecutiveFetchFailures++;
                            logger.LogWarning("Transient failure fetching icon for {ItemName} from {Url}, not incrementing FailCount",
                                icon.ItemName, match.IconUrl);
                        }
                    }
                    else
                    {
                        // No wiki match — permanent failure, increment FailCount
                        icon.FailCount++;
                        icon.LastAttemptAt = DateTimeOffset.UtcNow;
                        consecutiveFetchFailures = 0;
                        logger.LogWarning("No wiki match for {ItemName} (attempt {Attempt}/{Max})",
                            icon.ItemName, icon.FailCount, MaxFailAttempts);
                    }

                    await itemIconRepo.Save(icon);
                }
                catch (Exception ex)
                {
                    // Exception — transient failure, don't increment FailCount
                    logger.LogWarning(ex, "Transient error resolving icon for {ItemName}, not incrementing FailCount", icon.ItemName);
                    icon.LastAttemptAt = DateTimeOffset.UtcNow;
                    consecutiveFetchFailures++;
                    await itemIconRepo.Save(icon);
                }

                if (consecutiveFetchFailures >= 3)
                {
                    logger.LogWarning("Stopping cycle early: {Count} consecutive fetch failures detected (possible rate limiting)",
                        consecutiveFetchFailures);
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Item icon backfill cycle failed");
        }
    }
}
