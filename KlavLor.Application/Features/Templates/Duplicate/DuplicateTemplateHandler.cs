using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.Duplicate;

public sealed class DuplicateTemplateHandler(ITemplateRepository templateRepository)
{
    public async Task<Result<Template>> Handle(DuplicateTemplateCommand command, int userId)
    {
        var source = await templateRepository.GetById(command.SourceTemplateId);
        if (source is null)
            return Result<Template>.Failure("Template not found.");

        if (source.CreatedById != userId && !source.IsPublic)
            return Result<Template>.Failure("You do not have access to this template.");

        var newTemplate = new Template($"{source.Name} (Copy)", source.Description, userId)
        {
            IsPublic = false
        };

        // Copy groups
        var groupMap = new Dictionary<int, TemplateNodeGroup>();
        foreach (var sourceGroup in source.Groups)
        {
            var newGroup = newTemplate.AddGroup(sourceGroup.PositionX, sourceGroup.PositionY);
            groupMap[sourceGroup.Id] = newGroup;
        }

        // Copy nodes — use navigation property for group assignment since both are new entities
        var nodeMap = new Dictionary<int, TemplateNode>();
        foreach (var sourceNode in source.Nodes)
        {
            var newNode = newTemplate.AddNode(
                sourceNode.Label, sourceNode.NodeType,
                sourceNode.PositionX, sourceNode.PositionY,
                sourceNode.GearItemId, sourceNode.Metadata, sourceNode.IconUrl, color: sourceNode.Color);
            newNode.SortOrder = sourceNode.SortOrder;

            if (sourceNode.GroupId.HasValue && groupMap.TryGetValue(sourceNode.GroupId.Value, out var newGroup))
            {
                newNode.Group = newGroup;
            }
            else
            {
                // Standalone node — wrap in a new group
                var soloGroup = newTemplate.AddGroup(sourceNode.PositionX, sourceNode.PositionY);
                newNode.Group = soloGroup;
            }

            nodeMap[sourceNode.Id] = newNode;
        }

        // Save to get IDs assigned
        await templateRepository.SaveTemplate(newTemplate);

        // Copy edges using mapped IDs
        foreach (var sourceEdge in source.Edges)
        {
            if (nodeMap.TryGetValue(sourceEdge.FromNodeId, out var fromNode) &&
                nodeMap.TryGetValue(sourceEdge.ToNodeId, out var toNode))
            {
                newTemplate.AddEdge(fromNode.Id, toNode.Id);
            }
        }

        await templateRepository.SaveTemplate(newTemplate);

        return Result<Template>.Success(newTemplate);
    }
}
