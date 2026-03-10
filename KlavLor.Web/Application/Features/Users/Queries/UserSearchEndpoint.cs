using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Users.Search;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Users.Queries;

public sealed class UserSearchEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.UsersSearch.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.Admin));
    }

    private static async Task<RazorComponentResult> Endpoint(
        [AsParameters] UserSearchQuery query,
        UserSearchHandler handler)
    {
        var result = await handler.Handle(query);

        return IResultExtensions.Component<UsersSearchGrid>(new { Result = result.Value });
    }
}
