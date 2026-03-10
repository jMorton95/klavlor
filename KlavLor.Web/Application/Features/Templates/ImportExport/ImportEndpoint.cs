using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Import;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.ImportExport;

public sealed class ImportEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.TemplatesImport.FromApi(), GetPage).RequireAuthorization(nameof(RoleName.User));
        return app.MapPost(AppRoutes.TemplatesImport.FromApi(), Endpoint).RequireAuthorization(nameof(RoleName.User));
    }

    private static RazorComponentResult GetPage()
    {
        return IResultExtensions.Component<ImportForm>(new { Command = new ImportTemplateCommand() });
    }

    private static async Task<Results<HtmxRedirectResult, RazorComponentResult>> Endpoint(
        [FromForm] ImportTemplateCommand command,
        ISessionStateManager sessionManager,
        ImportTemplateHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        var result = await handler.Handle(command, userId.Value);

        return result switch
        {
            { IsSuccess: true } => IResultExtensions.HtmxRedirect(AppRoutes.Builder.WithId(result.Value.Id)),
            { ValidationErrors: not null } => IResultExtensions.Component<ImportForm>(new { Command = command, result.ValidationErrors }),
            _ => IResultExtensions.Component<ImportForm>(new { Command = command, ErrorMessage = result.ErrorMessage })
        };
    }
}
