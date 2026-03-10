using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class BuilderEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.Builder.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<IResult> Endpoint(
        int id,
        ITemplateRepository templateRepository,
        ISessionStateManager sessionManager)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        var template = await templateRepository.GetById(id);
        if (template is null) return IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);

        if (template.CreatedById != userId.Value)
            return IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);

        return IResultExtensions.Component<BuilderPage>(new { Template = template });
    }
}
