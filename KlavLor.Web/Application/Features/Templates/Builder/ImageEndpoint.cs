using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class ImageEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.CachedImage.FromApi(), GetImage).AllowAnonymous();
    }

    private static async Task<IResult> GetImage(int imageId, ICachedImageRepository cachedImageRepository, HttpContext httpContext)
    {
        var image = await cachedImageRepository.GetById(imageId);
        if (image is null)
            return Results.NotFound();

        // Cached images are immutable — aggressive browser caching
        httpContext.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return Results.File(
            image.ImageData,
            image.ContentType,
            enableRangeProcessing: false);
    }
}
