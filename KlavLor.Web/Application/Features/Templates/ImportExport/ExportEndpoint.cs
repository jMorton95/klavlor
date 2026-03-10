using System.Text.Json;
using KlavLor.Application.Features.Templates.Export;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;

namespace KlavLor.Web.Application.Features.Templates.ImportExport;

public sealed class ExportEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.TemplatesExport.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<IResult> Endpoint(
        int id,
        ISessionStateManager sessionManager,
        ExportTemplateHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var result = await handler.Handle(id, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();

        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        return Microsoft.AspNetCore.Http.Results.File(
            bytes,
            "application/json",
            $"{result.Value.Name.Replace(" ", "_")}_export.json");
    }
}
