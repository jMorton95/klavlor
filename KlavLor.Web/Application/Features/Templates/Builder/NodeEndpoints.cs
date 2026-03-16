using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Builder.AddNode;
using KlavLor.Application.Features.Builder.UpdateNode;
using KlavLor.Application.Features.Builder.UpdateNodePosition;
using KlavLor.Application.Features.Builder.DeleteNode;
using KlavLor.Application.Features.Builder.ReorderNode;
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
        app.MapPost(AppRoutes.BuilderNodes.FromApi(), AddNode).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        app.MapGet(AppRoutes.BuilderNodeEdit.FromApi(), GetEditModal).RequireAuthorization(nameof(RoleName.User));
        app.MapPut(AppRoutes.BuilderNode.FromApi(), UpdateNode).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        app.MapPut(AppRoutes.BuilderNodePosition.FromApi(), UpdateNodePosition).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("position");
        app.MapPut(AppRoutes.BuilderNodeReorder.FromApi(), ReorderNode).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        return app.MapDelete(AppRoutes.BuilderNode.FromApi(), DeleteNode).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
    }

    private static IResult GetCreateModal(
        int id,
        [FromQuery] double posX,
        [FromQuery] double posY,
        [FromQuery] int? groupId,
        ISessionStateManager sessionManager)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        return IResultExtensions.Component<NodeCreateModal>(new
        {
            TemplateId = id,
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

        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var node = result.Value!;
        var groupId = node.GroupId!.Value;
        var group = template.Groups.First(g => g.Id == groupId);
        var groupNodes = template.Nodes.Where(n => n.GroupId == groupId).OrderBy(n => n.SortOrder).ThenBy(n => n.Id).ToList();

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
        if (template is null || (template.CreatedById != userId.Value && !sessionManager.IsUserSessionAdministrator()))
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
            GroupId = node.GroupId,
            CurrentColor = node.Color
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

        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var node = template.Nodes.FirstOrDefault(n => n.Id == command.NodeId);
        if (node?.GroupId is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var group = template.Groups.First(g => g.Id == node.GroupId.Value);
        var groupNodes = template.Nodes.Where(n => n.GroupId == node.GroupId.Value).OrderBy(n => n.SortOrder).ThenBy(n => n.Id).ToList();

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
        var result = await handler.Handle(command);

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
        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var removedEdgeIds = edgeIdsBefore.Except(template.Edges.Select(e => e.Id)).ToList();
        var groupStillExists = groupId.HasValue && template.Groups.Any(g => g.Id == groupId.Value);

        // Compute which group-pair connections were fully removed
        var groupPairsBefore = new HashSet<(int, int)>();
        foreach (var edge in templateBefore.Edges)
        {
            var fn = templateBefore.Nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
            var tn = templateBefore.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
            if (fn?.GroupId != null && tn?.GroupId != null && fn.GroupId != tn.GroupId)
                groupPairsBefore.Add((fn.GroupId.Value, tn.GroupId.Value));
        }

        var groupPairsAfter = new HashSet<(int, int)>();
        foreach (var edge in template.Edges)
        {
            var fn = template.Nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
            var tn = template.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
            if (fn?.GroupId != null && tn?.GroupId != null && fn.GroupId != tn.GroupId)
                groupPairsAfter.Add((fn.GroupId.Value, tn.GroupId.Value));
        }

        var removedGroupPairs = groupPairsBefore.Except(groupPairsAfter)
            .Select(p => new[] { p.Item1, p.Item2 })
            .ToList();

        return Microsoft.AspNetCore.Http.Results.Json(new
        {
            removedEdgeIds,
            removedGroupPairs,
            groupId,
            groupStillExists
        });
    }

    private static async Task<IResult> ReorderNode(
        int id, int nodeId,
        [FromQuery] string direction,
        ISessionStateManager sessionManager,
        ReorderNodeHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var command = new ReorderNodeCommand { TemplateId = id, NodeId = nodeId, Direction = direction };
        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var node = template.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node?.GroupId is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var group = template.Groups.First(g => g.Id == node.GroupId.Value);
        var groupNodes = template.Nodes.Where(n => n.GroupId == node.GroupId.Value).OrderBy(n => n.SortOrder).ThenBy(n => n.Id).ToList();

        return IResultExtensions.Component<BuilderGroup>(new
        {
            Group = group,
            TemplateId = id,
            GroupedNodes = groupNodes
        });
    }

    private const string WikiImagesPrefix = "https://oldschool.runescape.wiki/images/";

    private static async Task<string?> CacheIconUrl(string? iconUrl, IImageCacheService imageCacheService)
    {
        if (string.IsNullOrEmpty(iconUrl) || !iconUrl.StartsWith(WikiImagesPrefix))
            return iconUrl;

        var cached = await imageCacheService.GetOrCache(iconUrl);
        return cached is not null
            ? $"data:{cached.ContentType};base64,{Convert.ToBase64String(cached.ImageData)}"
            : null;
    }
}
