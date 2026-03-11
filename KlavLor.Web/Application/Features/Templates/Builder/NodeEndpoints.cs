using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Builder.AddNode;
using KlavLor.Application.Features.Builder.UpdateNode;
using KlavLor.Application.Features.Builder.UpdateNodePosition;
using KlavLor.Application.Features.Builder.DeleteNode;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class NodeEndpoints : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.BuilderNodeCreate.FromApi(), GetCreateModal).RequireAuthorization(nameof(RoleName.User));
        app.MapPost(AppRoutes.BuilderNodes.FromApi(), AddNode).RequireAuthorization(nameof(RoleName.User));
        app.MapGet(AppRoutes.BuilderNodeEdit.FromApi(), GetEditModal).RequireAuthorization(nameof(RoleName.User));
        app.MapPut(AppRoutes.BuilderNode.FromApi(), UpdateNode).RequireAuthorization(nameof(RoleName.User));
        app.MapPut(AppRoutes.BuilderNodePosition.FromApi(), UpdateNodePosition).RequireAuthorization(nameof(RoleName.User));
        return app.MapDelete(AppRoutes.BuilderNode.FromApi(), DeleteNode).RequireAuthorization(nameof(RoleName.User));
    }

    private static IResult GetCreateModal(
        int id,
        [FromQuery] int nodeType,
        [FromQuery] double posX,
        [FromQuery] double posY,
        ISessionStateManager sessionManager)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var nt = (Domain.Entities.NodeType)nodeType;
        return IResultExtensions.Component<NodeCreateModal>(new
        {
            TemplateId = id,
            DefaultLabel = nt.ToString(),
            DefaultNodeType = nt,
            PositionX = posX > 0 ? posX : 400,
            PositionY = posY > 0 ? posY : 300
        });
    }

    private static async Task<IResult> AddNode(
        [FromForm] AddNodeCommand command,
        ISessionStateManager sessionManager,
        AddNodeHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        return IResultExtensions.Component<BuilderCanvas>(new { Template = template });
    }

    private static async Task<IResult> GetEditModal(
        int id, int nodeId,
        ISessionStateManager sessionManager,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var template = await templateRepository.GetById(id);
        if (template is null || template.CreatedById != userId.Value)
            return Microsoft.AspNetCore.Http.Results.NotFound();

        var node = template.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        return IResultExtensions.Component<NodeEditModal>(new
        {
            TemplateId = id,
            NodeId = nodeId,
            Label = node.Label,
            IconUrl = node.IconUrl,
            CurrentNodeType = node.NodeType
        });
    }

    private static async Task<IResult> UpdateNode(
        [FromForm] UpdateNodeCommand command,
        ISessionStateManager sessionManager,
        UpdateNodeHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        return IResultExtensions.Component<BuilderCanvas>(new { Template = template });
    }

    private static async Task<IResult> UpdateNodePosition(
        int id, int nodeId,
        [FromBody] UpdateNodePositionCommand command,
        ISessionStateManager sessionManager,
        UpdateNodePositionHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        command.TemplateId = id;
        command.NodeId = nodeId;
        var result = await handler.Handle(command, userId.Value);

        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.NoContent()
            : Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);
    }

    private static async Task<IResult> DeleteNode(
        int id, int nodeId,
        ISessionStateManager sessionManager,
        DeleteNodeHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var command = new DeleteNodeCommand { TemplateId = id, NodeId = nodeId };
        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        return IResultExtensions.Component<BuilderCanvas>(new { Template = template });
    }
}
