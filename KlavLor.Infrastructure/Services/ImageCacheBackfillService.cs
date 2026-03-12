using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

public sealed class ImageCacheBackfillService(IServiceScopeFactory scopeFactory, ILogger<ImageCacheBackfillService> logger) : BackgroundService
{
    private const string WikiImagesPrefix = "https://oldschool.runescape.wiki/images/";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var imageCacheService = scope.ServiceProvider.GetRequiredService<IImageCacheService>();

            // Find all nodes with wiki icon URLs that haven't been converted yet
            var nodesWithWikiUrls = await db.TemplateNodes
                .Where(n => n.IconUrl != null && n.IconUrl.StartsWith(WikiImagesPrefix))
                .ToListAsync(stoppingToken);

            if (nodesWithWikiUrls.Count == 0)
            {
                logger.LogInformation("Image cache backfill: no nodes with wiki URLs found");
                return;
            }

            logger.LogInformation("Image cache backfill: found {Count} nodes with wiki URLs to cache", nodesWithWikiUrls.Count);

            var uniqueUrls = nodesWithWikiUrls.Select(n => n.IconUrl!).Distinct().ToList();
            var urlToLocalPath = new Dictionary<string, string>();

            foreach (var url in uniqueUrls)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var cached = await imageCacheService.GetOrCache(url);
                if (cached is not null)
                {
                    urlToLocalPath[url] = $"/api/images/{cached.Id}";
                    logger.LogInformation("Cached image: {Url} -> /api/images/{Id}", url, cached.Id);
                }
                else
                {
                    logger.LogWarning("Failed to cache image: {Url}", url);
                }

                // Rate limit: small delay between fetches
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }

            // Update all nodes to use local URLs
            var updated = 0;
            foreach (var node in nodesWithWikiUrls)
            {
                if (node.IconUrl is not null && urlToLocalPath.TryGetValue(node.IconUrl, out var localPath))
                {
                    node.IconUrl = localPath;
                    updated++;
                }
            }

            if (updated > 0)
            {
                await db.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Image cache backfill: updated {Count} nodes to use cached images", updated);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image cache backfill failed");
        }
    }
}
