using KlavLor.Application.Features.Users.Account;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Users.Account;

public sealed class SidebarAccountEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        // Fetched by the sidebar via hx-trigger="load" so the user lookup runs in its
        // own request scope. The layout must never query during SSR — its async init
        // would race the page body's queries on the request's shared scoped DbContext.
        return app.MapGet(AppRoutes.SidebarAccount.FromApi(), GetAccount)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("read");
    }

    private static async Task<IResult> GetAccount(SidebarAccountHandler handler)
    {
        var result = await handler.Handle();
        if (!result.IsSuccess)
        {
            // 204 keeps HTMX from swapping — the sidebar just keeps its empty
            // placeholder instead of flashing an error block.
            return TypedResults.NoContent();
        }

        return IResultExtensions.Component<SidebarAccountInfo>(new
        {
            result.Value!.Name,
            result.Value.Email
        });
    }
}
