using KlavLor.Application.Features.Builder.LoadBuilder;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class BuilderEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.Builder.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("read");
    }

    private static async Task<IResult> Endpoint(int id, LoadBuilderHandler handler)
    {
        var result = await handler.Handle(id);
        if (!result.IsSuccess)
            return IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);

        return IResultExtensions.Component<BuilderPage>(new { Template = result.Value });
    }
}
