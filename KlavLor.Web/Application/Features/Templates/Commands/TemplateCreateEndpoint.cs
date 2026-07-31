using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Templates.Create;
using KlavLor.Application.Features.Templates.Search;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Templates.Commands;

public sealed class TemplateCreateEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.TemplatesCreate.FromApi(), GetPage).RequireAuthorization(nameof(RoleName.User));
        return app.MapPost(AppRoutes.TemplatesCreate.FromApi(), Endpoint).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
    }

    private static async Task<IResult> GetPage(
        ISessionStateManager sessionManager,
        ITemplateSearchRepository searchRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var cloneOptions = await searchRepository.GetCloneOptions(userId.Value);
        return IResultExtensions.Component<TemplateForm>(new
        {
            Command = new TemplateCreateCommand(),
            CloneOptions = cloneOptions
        });
    }

    private static async Task<Results<HtmxRedirectResult, RazorComponentResult>> Endpoint(
        [FromForm] TemplateCreateCommand command,
        ISessionStateManager sessionManager,
        TemplateCreateHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        var result = await handler.Handle(command);

        return result switch
        {
            { IsSuccess: true } => IResultExtensions.HtmxRedirect(AppRoutes.Builder.WithId(result.Value!.Id)),
            { ValidationErrors: not null } => IResultExtensions.Component<TemplateForm>(new { Command = command, result.ValidationErrors }),
            _ => IResultExtensions.Component<TemplateForm>(new { Command = command, ErrorMessage = result.ErrorMessage })
        };
    }
}
