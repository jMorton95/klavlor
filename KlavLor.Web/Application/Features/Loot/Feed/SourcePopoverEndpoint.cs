using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Loot.Feed;

public sealed class SourcePopoverEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        // Feed is public, so the popover is too — same authorisation profile as the
        // feed page itself. Rate-limit via the anonymous policy (per-IP, 120/min)
        // since this fires on hover and a casual scroll could trigger several.
        return app.MapGet(AppRoutes.LootFeedSourcePopover.FromApi(), GetPopover)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous");
    }

    private static async Task<IResult> GetPopover(int id, string name, SourcePopoverHandler handler)
    {
        var result = await handler.Handle(id, name);
        if (!result.IsSuccess)
        {
            // 204 keeps HTMX silent on hover failures — we don't want a red flash on
            // a missing source. The client just gets an empty swap.
            return TypedResults.NoContent();
        }

        return IResultExtensions.Component<LootFeedSourcePopover>(new
        {
            CharacterId = id,
            Data = result.Value
        });
    }
}
