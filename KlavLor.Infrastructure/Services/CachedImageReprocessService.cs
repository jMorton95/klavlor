using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

/// One-shot migration that walks every cached image row, resizes + re-encodes to WebP,
/// and writes the smaller blob back in place. Profile is inferred from whether the image
/// is referenced by an ItemIcon or SourceIcon row; otherwise it's treated as a template asset.
public sealed class CachedImageReprocessService(IServiceScopeFactory scopeFactory, ILogger<CachedImageReprocessService> logger) : BackgroundService
{
    private const int PageSize = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app boot before doing CPU-heavy work
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        var cursor = 0;
        var processed = 0;
        var failed = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();

                var page = await db.CachedImages
                    .Where(c => c.Id > cursor && c.ContentType != WebpEncoder.ContentType)
                    .OrderBy(c => c.Id)
                    .Take(PageSize)
                    .ToListAsync(stoppingToken);

                if (page.Count == 0)
                {
                    logger.LogInformation("CachedImageReprocessService: done. Re-encoded {Processed} images ({Failed} skipped)", processed, failed);
                    return;
                }

                cursor = page.Max(p => p.Id);

                var ids = page.Select(c => c.Id).ToList();
                var itemSet = (await db.ItemIcons
                    .Where(i => i.CachedImageId != null && ids.Contains(i.CachedImageId.Value))
                    .Select(i => i.CachedImageId!.Value)
                    .ToListAsync(stoppingToken)).ToHashSet();
                var sourceSet = (await db.SourceIcons
                    .Where(s => s.CachedImageId != null && ids.Contains(s.CachedImageId.Value))
                    .Select(s => s.CachedImageId!.Value)
                    .ToListAsync(stoppingToken)).ToHashSet();

                var anyChanged = false;
                foreach (var img in page)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    var profile = itemSet.Contains(img.Id) ? ImageProfile.ItemIcon
                        : sourceSet.Contains(img.Id) ? ImageProfile.SourceIcon
                        : ImageProfile.TemplateAsset;

                    try
                    {
                        var encoded = WebpEncoder.TryEncode(img.ImageData, profile);
                        if (encoded is null)
                        {
                            logger.LogWarning("CachedImage {Id} (source={Source}) could not be decoded — leaving as-is",
                                img.Id, img.SourceUrl);
                            failed++;
                            continue;
                        }

                        var originalSize = img.ImageData.Length;
                        img.ImageData = encoded;
                        img.ContentType = WebpEncoder.ContentType;
                        anyChanged = true;
                        processed++;
                        logger.LogInformation("CachedImage {Id} reprocessed as {Profile} ({Original}→{New} bytes)",
                            img.Id, profile, originalSize, encoded.Length);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed reprocessing CachedImage {Id}", img.Id);
                        failed++;
                    }
                }

                if (anyChanged)
                    await db.SaveChangesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CachedImageReprocessService failed");
        }
    }
}
