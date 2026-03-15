using KlavLor.Application.Features.Builder.ApplyAutoLayout;
using KlavLor.Application.Features.Builder.UndoLayout;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class LayoutEndpoints : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost(AppRoutes.BuilderAutoLayout.FromApi(), ApplyAutoLayout).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        return app.MapPost(AppRoutes.BuilderUndoLayout.FromApi(), UndoLayout).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
    }

    private static async Task<IResult> ApplyAutoLayout(
        int id,
        ISessionStateManager sessionManager,
        ApplyAutoLayoutHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var command = new ApplyAutoLayoutCommand { TemplateId = id };
        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        return IResultExtensions.Component<BuilderPage>(new { Template = template });
    }

    private static async Task<IResult> UndoLayout(
        int id,
        ISessionStateManager sessionManager,
        UndoLayoutHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var command = new UndoLayoutCommand { TemplateId = id };
        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        return IResultExtensions.Component<BuilderPage>(new { Template = template });
    }
}
