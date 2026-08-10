using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.CollectionLog;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.CollectionLog;

/// <summary>
/// The Collection Log area. Authenticated like the other cross-character comparison surfaces (the
/// global source and drop pages) rather than public: it exposes every tracked character's progress
/// side by side, which is clan-internal.
/// </summary>
/// <remarks>
/// MUST be added to the explicit list in ConfigureEndpoints.cs — endpoint classes are not
/// auto-registered here, and an unregistered one silently 404s.
/// </remarks>
public sealed class CollectionLogEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.CollectionLog.FromApi(), GetOverview)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>();

        app.MapGet(AppRoutes.CollectionLogCharacter.FromApi(), GetCharacter)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>();

        // Panel, not a page: swaps the item grid inside the character page when a category is picked.
        app.MapGet(AppRoutes.CollectionLogCharacterCategory.FromApi(), GetCharacterCategory)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.CollectionLogCategory.FromApi(), GetCategoryComparison)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>();

        app.MapGet(AppRoutes.CollectionLogItem.FromApi(), GetItemComparison)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>();

        return app.MapGet(AppRoutes.CollectionLogSearch.FromApi(), Search)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<RazorComponentResult> GetOverview(CollectionLogHandler handler)
    {
        var standings = await handler.GetStandings();
        return IResultExtensions.Component<CollectionLogOverview>(new { Standings = standings });
    }

    private static async Task<RazorComponentResult> GetCharacter(int id, CollectionLogHandler handler)
    {
        var log = await handler.GetCharacterLog(id);
        return IResultExtensions.Component<CollectionLogCharacterDetail>(new { Log = log });
    }

    private static async Task<RazorComponentResult> GetCharacterCategory(
        int id,
        [FromQuery] string slug,
        CollectionLogHandler handler)
    {
        var items = await handler.GetCategoryItems(id, slug);
        return IResultExtensions.Component<CollectionLogItemGrid>(new
        {
            Items = items,
            CategorySlug = slug
        });
    }

    private static async Task<RazorComponentResult> GetCategoryComparison(
        [FromQuery] string slug,
        CollectionLogHandler handler)
    {
        var comparison = await handler.GetCategoryComparison(slug);
        return IResultExtensions.Component<CollectionLogCategoryDetail>(new { Comparison = comparison, Slug = slug });
    }

    private static async Task<RazorComponentResult> GetItemComparison(int itemId, CollectionLogHandler handler)
    {
        var comparison = await handler.GetItemComparison(itemId);
        return IResultExtensions.Component<CollectionLogItemDetail>(new { Comparison = comparison });
    }

    private static async Task<RazorComponentResult> Search(
        [FromQuery] string? searchTerm,
        CollectionLogHandler handler)
    {
        var rows = await handler.SearchItems(searchTerm);
        return IResultExtensions.Component<CollectionLogSearchResults>(new { Rows = rows, SearchTerm = searchTerm });
    }
}
