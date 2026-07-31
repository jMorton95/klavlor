using KlavLor.Application.Features.Viewer.ToggleCompletion;
using KlavLor.Application.Features.Viewer.ViewerData;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Viewer;

public sealed class CompletionEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapPost(AppRoutes.ViewerCompletion.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("mutation");
    }

    private static async Task<IResult> Endpoint(
        int id,
        int nodeId,
        [AsParameters] CompletionForm form,
        ISessionStateManager sessionManager,
        ToggleCompletionHandler toggleHandler,
        ITemplateRepository templateRepository,
        IUserNodeCompletionRepository completionRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var command = new ToggleCompletionCommand { TemplateId = id, NodeId = nodeId, Note = form.Note };
        var result = await toggleHandler.Handle(command);
        if (!result.IsSuccess) return Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        if (template is null) return Results.NotFound();

        var node = template.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return Results.NotFound();

        var allCompletions = await completionRepository.GetByUserAndTemplate(userId.Value, id);
        var completionDates = allCompletions.ToDictionary(
            c => c.TemplateNodeId,
            c => new CompletionInfo(c.CompletedAt, c.Note));

        var completion = await completionRepository.GetCompletion(userId.Value, nodeId);
        var isCompleted = completion is not null;
        DateTimeOffset? completedAt = completion?.CompletedAt;
        string? completionNote = completion?.Note;

        // Compute "What's Next" for this node
        var isNext = false;
        if (!isCompleted && node.GroupId.HasValue)
        {
            var completedNodeIds = allCompletions.Select(c => c.TemplateNodeId).ToHashSet();
            var nodesByGroup = template.Nodes
                .Where(n => n.GroupId.HasValue)
                .GroupBy(n => n.GroupId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Build predecessor groups
            var predecessorGroups = new Dictionary<int, HashSet<int>>();
            foreach (var group in template.Groups)
                predecessorGroups[group.Id] = [];
            foreach (var edge in template.Edges)
            {
                var fromNode = template.Nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
                var toNode = template.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
                if (fromNode?.GroupId != null && toNode?.GroupId != null && fromNode.GroupId != toNode.GroupId)
                {
                    if (predecessorGroups.TryGetValue(toNode.GroupId.Value, out var preds))
                        preds.Add(fromNode.GroupId.Value);
                }
            }

            var preds2 = predecessorGroups.GetValueOrDefault(node.GroupId.Value);
            var allPredsCompleted = true;
            if (preds2 is { Count: > 0 })
            {
                foreach (var predGid in preds2)
                {
                    var predNodes = nodesByGroup.GetValueOrDefault(predGid);
                    if (predNodes == null || !predNodes.All(n => completedNodeIds.Contains(n.Id)))
                    {
                        allPredsCompleted = false;
                        break;
                    }
                }
            }
            isNext = allPredsCompleted;
        }

        return IResultExtensions.Component<CompletionToggleResult>(new
        {
            Node = node,
            Template = template,
            TemplateId = id,
            IsCompleted = isCompleted,
            CompletedAt = completedAt,
            CompletionNote = completionNote,
            IsNext = isNext,
            CompletionDates = completionDates
        });
    }

    private sealed class CompletionForm
    {
        [Microsoft.AspNetCore.Mvc.FromForm]
        public string? Note { get; set; }
    }
}
