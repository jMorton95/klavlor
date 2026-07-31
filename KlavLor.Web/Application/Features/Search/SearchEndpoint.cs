using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Search;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Search;

public sealed class SearchEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        // Shell: the page body swapped into #hx-page-container on navigation. Pushes
        // /search?searchTerm=… so the searched URL is shareable/back-navigable.
        app.MapGet(AppRoutes.Search.FromApi(), GetShell)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>();

        // The row of section placeholders, re-requested by the debounced input so each
        // section re-fires its own hx-trigger="load" with the new query.
        app.MapGet(AppRoutes.SearchSections.FromApi(), GetSections)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.SearchSectionCharacters.FromApi(), GetCharacters)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.SearchSectionSources.FromApi(), GetSources)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.SearchSectionDrops.FromApi(), GetDrops)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.SearchSectionItems.FromApi(), GetItems)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.SearchSectionTemplates.FromApi(), GetTemplates)
            .RequireAuthorization(nameof(RoleName.User));

        return app.MapGet(AppRoutes.SearchSectionUsers.FromApi(), GetUsers)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static RazorComponentResult GetShell([AsParameters] SearchQuery query)
        => IResultExtensions.Component<SearchShell>(new { SearchTerm = query.SearchTerm });

    private static RazorComponentResult GetSections([AsParameters] SearchQuery query)
        => IResultExtensions.Component<SearchSections>(new { SearchTerm = query.SearchTerm });

    private static async Task<RazorComponentResult> GetCharacters([AsParameters] SearchQuery query, SearchHandler handler)
    {
        var results = await handler.SearchCharacters(query.SearchTerm);
        return IResultExtensions.Component<SearchCharactersSection>(new { Results = results });
    }

    private static async Task<RazorComponentResult> GetSources([AsParameters] SearchQuery query, SearchHandler handler)
    {
        var results = await handler.SearchSources(query.SearchTerm);
        return IResultExtensions.Component<SearchSourcesSection>(new { Results = results });
    }

    private static async Task<RazorComponentResult> GetDrops([AsParameters] SearchQuery query, SearchHandler handler)
    {
        var results = await handler.SearchDrops(query.SearchTerm);
        return IResultExtensions.Component<SearchDropsSection>(new { Results = results });
    }

    private static async Task<RazorComponentResult> GetItems([AsParameters] SearchQuery query, SearchHandler handler)
    {
        var results = await handler.SearchItemCatalog(query.SearchTerm);
        return IResultExtensions.Component<SearchItemsSection>(new { Results = results });
    }

    private static async Task<RazorComponentResult> GetTemplates([AsParameters] SearchQuery query, SearchHandler handler)
    {
        var results = await handler.SearchTemplates(query.SearchTerm);
        return IResultExtensions.Component<SearchTemplatesSection>(new { Results = results });
    }

    private static async Task<RazorComponentResult> GetUsers([AsParameters] SearchQuery query, SearchHandler handler)
    {
        var results = await handler.SearchUsers(query.SearchTerm);
        return IResultExtensions.Component<SearchUsersSection>(new { Results = results });
    }
}
