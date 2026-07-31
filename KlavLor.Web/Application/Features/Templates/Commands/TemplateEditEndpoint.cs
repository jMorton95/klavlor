using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Templates.Edit;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Templates.Commands;

public sealed class TemplateEditEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.TemplatesEdit.FromApi(), GetPage).RequireAuthorization(nameof(RoleName.User));
        return app.MapPost(AppRoutes.TemplatesEdit.FromApi(), Endpoint).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
    }

    private static async Task<Results<RazorComponentResult, HtmxRedirectResult>> GetPage(
        int id, ISessionStateManager sessionManager, ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        var template = await templateRepository.GetById(id);

        if (template is null || (template.CreatedById != userId.Value && !sessionManager.IsUserSessionAdministrator()))
            return IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);

        var command = new TemplateEditCommand
        {
            Id = template.Id,
            Name = template.Name,
            Description = template.Description,
            IsPublic = template.IsPublic
        };

        return IResultExtensions.Component<TemplateForm>(new { Command = command, IsEditing = true });
    }

    private static async Task<Results<HtmxRedirectResult, RazorComponentResult>> Endpoint(
        int id,
        [FromForm] TemplateEditCommand command,
        ISessionStateManager sessionManager,
        TemplateEditHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        command.Id = id;
        var result = await handler.Handle(command);

        return result switch
        {
            { IsSuccess: true } => IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch),
            { ValidationErrors: not null } => IResultExtensions.Component<TemplateForm>(new { Command = command, result.ValidationErrors, IsEditing = true }),
            _ => IResultExtensions.Component<TemplateForm>(new { Command = command, ErrorMessage = result.ErrorMessage, IsEditing = true })
        };
    }
}
