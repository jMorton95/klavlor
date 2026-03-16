using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Builder.AddEdge;
using KlavLor.Application.Features.Builder.DeleteEdge;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class EdgeEndpoints : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost(AppRoutes.BuilderEdges.FromApi(), AddEdge).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        return app.MapDelete(AppRoutes.BuilderEdge.FromApi(), DeleteEdge).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
    }

    private static async Task<IResult> AddEdge(
        [FromForm] AddEdgeCommand command,
        ISessionStateManager sessionManager,
        AddEdgeHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        return Microsoft.AspNetCore.Http.Results.Ok(new { success = true });
    }

    private static async Task<IResult> DeleteEdge(
        int id, int edgeId,
        ISessionStateManager sessionManager,
        DeleteEdgeHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var command = new DeleteEdgeCommand { TemplateId = id, EdgeId = edgeId };
        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        return Microsoft.AspNetCore.Http.Results.NoContent();
    }
}
