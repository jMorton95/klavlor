using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Web.Application.Features.Loot;

public sealed class SourceIconEndpoint : IEndpoint
{
    private record CachedIconResult(byte[]? ImageData, string? ContentType);

    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.SourceIcon.FromApi(), GetSourceIcon)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous");
    }

    private static async Task<IResult> GetSourceIcon(
        [FromQuery] string name,
        ISourceIconRepository sourceIconRepository,
        ICachedImageRepository cachedImageRepository,
        IMemoryCache memoryCache,
        HttpContext httpContext)
    {
        var cacheKey = $"source-icon:{name}";

        if (memoryCache.TryGetValue(cacheKey, out CachedIconResult? cached))
        {
            if (cached?.ImageData is null)
            {
                httpContext.Response.Headers.CacheControl = "no-cache";
                return Microsoft.AspNetCore.Http.Results.NotFound();
            }

            httpContext.Response.Headers.CacheControl = "public, max-age=604800";
            return Microsoft.AspNetCore.Http.Results.File(cached.ImageData, cached.ContentType!);
        }

        var icon = await sourceIconRepository.GetBySourceName(name);
        if (icon?.CachedImageId is null)
        {
            memoryCache.Set(cacheKey, new CachedIconResult(null, null), TimeSpan.FromMinutes(5));
            httpContext.Response.Headers.CacheControl = "no-cache";
            return Microsoft.AspNetCore.Http.Results.NotFound();
        }

        var image = await cachedImageRepository.GetById(icon.CachedImageId.Value);
        if (image is null)
        {
            memoryCache.Set(cacheKey, new CachedIconResult(null, null), TimeSpan.FromMinutes(5));
            httpContext.Response.Headers.CacheControl = "no-cache";
            return Microsoft.AspNetCore.Http.Results.NotFound();
        }

        memoryCache.Set(cacheKey, new CachedIconResult(image.ImageData, image.ContentType), TimeSpan.FromHours(1));
        httpContext.Response.Headers.CacheControl = "public, max-age=604800";
        return Microsoft.AspNetCore.Http.Results.File(image.ImageData, image.ContentType);
    }
}
