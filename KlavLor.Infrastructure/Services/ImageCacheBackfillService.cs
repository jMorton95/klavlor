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
    private const string DataUriPrefix = "data:";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var imageCacheService = scope.ServiceProvider.GetRequiredService<IImageCacheService>();

            // Find nodes with wiki URLs that need caching, or data URIs that should be converted to /api/images/ refs
            var nodesToConvert = await db.TemplateNodes
                .Where(n => n.IconUrl != null &&
                    (n.IconUrl.StartsWith(WikiImagesPrefix) || n.IconUrl.StartsWith(DataUriPrefix)))
                .ToListAsync(stoppingToken);

            if (nodesToConvert.Count == 0)
            {
                logger.LogInformation("Image cache backfill: no nodes to convert");
                return;
            }

            logger.LogInformation("Image cache backfill: found {Count} nodes to convert", nodesToConvert.Count);

            // Convert data URI nodes back to /api/images/ references
            var dataUriNodes = nodesToConvert.Where(n => n.IconUrl!.StartsWith(DataUriPrefix)).ToList();
            if (dataUriNodes.Count > 0)
            {
                var cachedImageRepo = scope.ServiceProvider.GetRequiredService<Domain.Interfaces.Repositories.ICachedImageRepository>();
                foreach (var node in dataUriNodes)
                {
                    var dataUri = node.IconUrl!;
                    var cached = await imageCacheService.GetOrCacheFromDataUri(dataUri, ImageProfile.TemplateAsset);
                    if (cached is not null)
                    {
                        node.IconUrl = $"/api/images/{cached.Id}";
                        logger.LogInformation("Converted data URI to /api/images/{ImageId} for node {NodeId}", cached.Id, node.Id);
                    }
                }
            }

            // Cache wiki URLs and convert to /api/images/ references
            var wikiNodes = nodesToConvert.Where(n => n.IconUrl!.StartsWith(WikiImagesPrefix)).ToList();
            foreach (var node in wikiNodes)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var cached = await imageCacheService.GetOrCache(node.IconUrl!, ImageProfile.TemplateAsset);
                if (cached is not null)
                {
                    node.IconUrl = $"/api/images/{cached.Id}";
                    logger.LogInformation("Cached wiki image as /api/images/{ImageId} for node {NodeId}", cached.Id, node.Id);
                }
                else
                {
                    logger.LogWarning("Permanently skipping unfetchable image, clearing URL: {Url}", node.IconUrl);
                    node.IconUrl = null;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }

            await db.SaveChangesAsync(stoppingToken);
            logger.LogInformation("Image cache backfill: converted {Count} nodes", nodesToConvert.Count);
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
