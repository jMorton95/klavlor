using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

public sealed class SourceIconBackfillService(IServiceScopeFactory scopeFactory, ILogger<SourceIconBackfillService> logger) : BackgroundService
{
    private const int MaxFailAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app startup
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));

        try
        {
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
            logger.LogError(ex, "Source icon backfill service failed unexpectedly");
        }
    }

    private async Task RunCycle(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sourceIconRepo = scope.ServiceProvider.GetRequiredService<ISourceIconRepository>();
            var wikiClient = scope.ServiceProvider.GetRequiredService<IOsrsWikiClient>();
            var imageCacheService = scope.ServiceProvider.GetRequiredService<IImageCacheService>();

            // Step 1: Discover new sources from loot records
            var uncatalogued = await sourceIconRepo.FindUncataloguedSources(50);
            if (uncatalogued.Count > 0)
            {
                logger.LogInformation("Source icon backfill: discovered {Count} new sources", uncatalogued.Count);

                var newIcons = uncatalogued.Select(name => new SourceIcon
                {
                    SourceName = name
                }).ToList();

                await sourceIconRepo.SaveRange(newIcons);
            }

            // Step 2: Resolve pending icons (no cached image yet, under fail threshold)
            var pending = await sourceIconRepo.GetPendingIcons(50);
            if (pending.Count == 0)
            {
                if (uncatalogued.Count == 0)
                    logger.LogDebug("Source icon backfill: nothing to do");
                return;
            }

            logger.LogInformation("Source icon backfill: resolving {Count} pending icons", pending.Count);

            var consecutiveFetchFailures = 0;

            foreach (var icon in pending)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var results = await wikiClient.SearchItems(icon.SourceName, limit: 5);

                    // Pick best match: exact name match first, then first result
                    var match = results.FirstOrDefault(r => r.Name.Equals(icon.SourceName, StringComparison.OrdinalIgnoreCase))
                                ?? results.FirstOrDefault();

                    if (match?.IconUrl is not null)
                    {
                        var cached = await imageCacheService.GetOrCache(match.IconUrl, ImageProfile.SourceIcon);
                        if (cached is not null)
                        {
                            icon.CachedImageId = cached.Id;
                            consecutiveFetchFailures = 0;
                            logger.LogInformation("Cached source icon for {SourceName} as image {ImageId}", icon.SourceName, cached.Id);
                        }
                        else
                        {
                            icon.LastAttemptAt = DateTimeOffset.UtcNow;
                            consecutiveFetchFailures++;
                            logger.LogWarning("Transient failure fetching source icon for {SourceName} from {Url}",
                                icon.SourceName, match.IconUrl);
                        }
                    }
                    else
                    {
                        icon.FailCount++;
                        icon.LastAttemptAt = DateTimeOffset.UtcNow;
                        consecutiveFetchFailures = 0;
                        logger.LogWarning("No wiki match for source {SourceName} (attempt {Attempt}/{Max})",
                            icon.SourceName, icon.FailCount, MaxFailAttempts);
                    }

                    await sourceIconRepo.Save(icon);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Transient error resolving source icon for {SourceName}", icon.SourceName);
                    icon.LastAttemptAt = DateTimeOffset.UtcNow;
                    consecutiveFetchFailures++;
                    await sourceIconRepo.Save(icon);
                }

                if (consecutiveFetchFailures >= 3)
                {
                    logger.LogWarning("Stopping cycle early: {Count} consecutive fetch failures detected (possible rate limiting)",
                        consecutiveFetchFailures);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Source icon backfill cycle failed");
        }
    }
}
