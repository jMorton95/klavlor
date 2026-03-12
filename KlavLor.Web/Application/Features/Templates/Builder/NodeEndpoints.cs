using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Builder.AddNode;
using KlavLor.Application.Features.Builder.UpdateNode;
using KlavLor.Application.Features.Builder.UpdateNodePosition;
using KlavLor.Application.Features.Builder.DeleteNode;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
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
        [FromQuery] int? groupId,
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
            PositionY = posY > 0 ? posY : 300,
            GroupId = groupId
        });
    }

    private static async Task<IResult> AddNode(
        [FromForm] AddNodeCommand command,
        ISessionStateManager sessionManager,
        AddNodeHandler handler,
        ITemplateRepository templateRepository,
        IImageCacheService imageCacheService)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        // Cache wiki image and use local URL
        command.IconUrl = await CacheIconUrl(command.IconUrl, imageCacheService);

        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var node = result.Value!;
        var groupId = node.GroupId!.Value;
        var group = template.Groups.First(g => g.Id == groupId);
        var groupNodes = template.Nodes.Where(n => n.GroupId == groupId).ToList();

        return IResultExtensions.Component<BuilderGroup>(new
        {
            Group = group,
            TemplateId = command.TemplateId,
            GroupedNodes = groupNodes
        });
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
            CurrentNodeType = node.NodeType,
            GroupId = node.GroupId
        });
    }

    private static async Task<IResult> UpdateNode(
        [FromForm] UpdateNodeCommand command,
        ISessionStateManager sessionManager,
        UpdateNodeHandler handler,
        ITemplateRepository templateRepository,
        IImageCacheService imageCacheService)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        // Cache wiki image and use local URL
        command.IconUrl = await CacheIconUrl(command.IconUrl, imageCacheService);

        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var node = template.Nodes.FirstOrDefault(n => n.Id == command.NodeId);
        if (node?.GroupId is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var group = template.Groups.First(g => g.Id == node.GroupId.Value);
        var groupNodes = template.Nodes.Where(n => n.GroupId == node.GroupId.Value).ToList();

        return IResultExtensions.Component<BuilderGroup>(new
        {
            Group = group,
            TemplateId = command.TemplateId,
            GroupedNodes = groupNodes
        });
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

        // Capture state before deletion
        var templateBefore = await templateRepository.GetById(id);
        if (templateBefore is null) return Microsoft.AspNetCore.Http.Results.NotFound();
        var nodeToDelete = templateBefore.Nodes.FirstOrDefault(n => n.Id == nodeId);
        var groupId = nodeToDelete?.GroupId;
        var edgeIdsBefore = templateBefore.Edges.Select(e => e.Id).ToHashSet();

        var command = new DeleteNodeCommand { TemplateId = id, NodeId = nodeId };
        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var removedEdgeIds = edgeIdsBefore.Except(template.Edges.Select(e => e.Id)).ToList();
        var groupStillExists = groupId.HasValue && template.Groups.Any(g => g.Id == groupId.Value);

        return Microsoft.AspNetCore.Http.Results.Json(new
        {
            removedEdgeIds,
            groupId,
            groupStillExists
        });
    }

    private const string WikiImagesPrefix = "https://oldschool.runescape.wiki/images/";

    private static async Task<string?> CacheIconUrl(string? iconUrl, IImageCacheService imageCacheService)
    {
        if (string.IsNullOrEmpty(iconUrl) || !iconUrl.StartsWith(WikiImagesPrefix))
            return iconUrl;

        var cached = await imageCacheService.GetOrCache(iconUrl);
        return cached is not null ? $"/api/images/{cached.Id}" : iconUrl;
    }
}
