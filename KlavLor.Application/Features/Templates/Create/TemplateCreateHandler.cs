using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.Create;

public sealed class TemplateCreateHandler(
    ITemplateRepository templateRepository,
    TemplateCreateValidator validator)
{
    public async Task<Result<Template>> Handle(TemplateCreateCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result<Template>.ValidationFailure(validationResult.ToDictionary());

        var template = new Template(command.Name, command.Description, userId)
        {
            IsPublic = command.IsPublic
        };

        if (command.SourceTemplateId.HasValue)
        {
            var source = await templateRepository.GetById(command.SourceTemplateId.Value);
            if (source is null)
                return Result<Template>.Failure("Source template not found.");

            if (source.CreatedById != userId && !source.IsPublic)
                return Result<Template>.Failure("You do not have access to this template.");

            if (source.Nodes.Count > 500 || source.Edges.Count > 2000)
                return Result<Template>.Failure("Source template is too large to clone.");

            // Copy groups
            var groupMap = new Dictionary<int, TemplateNodeGroup>();
            foreach (var sourceGroup in source.Groups)
            {
                var newGroup = template.AddGroup(sourceGroup.PositionX, sourceGroup.PositionY);
                groupMap[sourceGroup.Id] = newGroup;
            }

            // Copy nodes with group assignment via navigation property
            var nodeMap = new Dictionary<int, TemplateNode>();
            foreach (var sourceNode in source.Nodes)
            {
                var newNode = template.AddNode(
                    sourceNode.Label, sourceNode.NodeType,
                    sourceNode.PositionX, sourceNode.PositionY,
                    sourceNode.GearItemId, sourceNode.Metadata, sourceNode.IconUrl);

                if (sourceNode.GroupId.HasValue && groupMap.TryGetValue(sourceNode.GroupId.Value, out var newGroup))
                {
                    newNode.Group = newGroup;
                }
                else
                {
                    var soloGroup = template.AddGroup(sourceNode.PositionX, sourceNode.PositionY);
                    newNode.Group = soloGroup;
                }

                nodeMap[sourceNode.Id] = newNode;
            }

            // Copy annotations
            foreach (var sourceAnnotation in source.Annotations)
            {
                template.AddAnnotation(sourceAnnotation.Text, sourceAnnotation.PositionX, sourceAnnotation.PositionY, sourceAnnotation.FontSize);
            }

            // Copy regions
            foreach (var sourceRegion in source.Regions)
            {
                template.AddRegion(sourceRegion.PositionX, sourceRegion.PositionY, sourceRegion.Width, sourceRegion.Height, sourceRegion.Color, sourceRegion.Opacity, sourceRegion.Label);
            }

            // Save to get IDs, then copy edges
            await templateRepository.SaveTemplate(template);

            foreach (var sourceEdge in source.Edges)
            {
                if (nodeMap.TryGetValue(sourceEdge.FromNodeId, out var fromNode) &&
                    nodeMap.TryGetValue(sourceEdge.ToNodeId, out var toNode))
                {
                    template.AddEdge(fromNode.Id, toNode.Id);
                }
            }
        }

        await templateRepository.SaveTemplate(template);

        return Result<Template>.Success(template);
    }
}
