using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Builder.AddEdge;
using KlavLor.Application.Features.Builder.DeleteEdge;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class EdgeEndpoints : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost(AppRoutes.BuilderEdges.FromApi(), AddEdge).RequireAuthorization(nameof(RoleName.User));
        return app.MapDelete(AppRoutes.BuilderEdge.FromApi(), DeleteEdge).RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<IResult> AddEdge(
        [FromForm] AddEdgeCommand command,
        ISessionStateManager sessionManager,
        AddEdgeHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        return IResultExtensions.Component<BuilderCanvas>(new { Template = template });
    }

    private static async Task<IResult> DeleteEdge(
        int id, int edgeId,
        ISessionStateManager sessionManager,
        DeleteEdgeHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var command = new DeleteEdgeCommand { TemplateId = id, EdgeId = edgeId };
        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        return IResultExtensions.Component<BuilderCanvas>(new { Template = template });
    }
}
