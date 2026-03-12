using System;
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
    private const string ApiImagesPrefix = "/api/images/";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var imageCacheService = scope.ServiceProvider.GetRequiredService<IImageCacheService>();

            // Find nodes with wiki URLs or /api/images/ URLs that need converting to data URIs
            var nodesToConvert = await db.TemplateNodes
                .Where(n => n.IconUrl != null &&
                    (n.IconUrl.StartsWith(WikiImagesPrefix) || n.IconUrl.StartsWith(ApiImagesPrefix)))
                .ToListAsync(stoppingToken);

            if (nodesToConvert.Count == 0)
            {
                logger.LogInformation("Image cache backfill: no nodes to convert");
                return;
            }

            logger.LogInformation("Image cache backfill: found {Count} nodes to convert to data URIs", nodesToConvert.Count);

            // Collect unique wiki URLs to fetch
            var wikiUrls = nodesToConvert
                .Where(n => n.IconUrl!.StartsWith(WikiImagesPrefix))
                .Select(n => n.IconUrl!)
                .Distinct()
                .ToList();

            var urlToDataUri = new Dictionary<string, string>();

            foreach (var url in wikiUrls)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var cached = await imageCacheService.GetOrCache(url);
                if (cached is not null)
                {
                    urlToDataUri[url] = $"data:{cached.ContentType};base64,{Convert.ToBase64String(cached.ImageData)}";
                    logger.LogInformation("Cached image: {Url}", url);
                }
                else
                {
                    logger.LogWarning("Failed to cache image: {Url}", url);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }

            // For /api/images/{id} URLs, look up the cached image from DB
            var apiImageNodes = nodesToConvert.Where(n => n.IconUrl!.StartsWith(ApiImagesPrefix)).ToList();
            if (apiImageNodes.Count > 0)
            {
                var cachedImageRepo = scope.ServiceProvider.GetRequiredService<Domain.Interfaces.Repositories.ICachedImageRepository>();
                foreach (var node in apiImageNodes)
                {
                    var idStr = node.IconUrl!.Replace(ApiImagesPrefix, "");
                    if (int.TryParse(idStr, out var imageId))
                    {
                        var cached = await cachedImageRepo.GetById(imageId);
                        if (cached is not null)
                        {
                            node.IconUrl = $"data:{cached.ContentType};base64,{Convert.ToBase64String(cached.ImageData)}";
                        }
                    }
                }
            }

            // Update wiki URL nodes
            foreach (var node in nodesToConvert.Where(n => n.IconUrl!.StartsWith(WikiImagesPrefix)))
            {
                if (urlToDataUri.TryGetValue(node.IconUrl!, out var dataUri))
                    node.IconUrl = dataUri;
            }

            await db.SaveChangesAsync(stoppingToken);
            logger.LogInformation("Image cache backfill: converted {Count} nodes to data URIs", nodesToConvert.Count);
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
