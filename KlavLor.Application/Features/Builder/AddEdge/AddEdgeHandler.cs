using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.AddEdge;

public sealed class AddEdgeHandler(
    ITemplateRepository templateRepository,
    AddEdgeValidator validator)
{
    public async Task<Result> Handle(AddEdgeCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result.ValidationFailure(validationResult.ToDictionary());

        var template = await templateRepository.GetById(command.TemplateId);

        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result.Failure("You do not have permission to modify this template.");

        if (template.Edges.Count >= 2000)
            return Result.Failure("Maximum of 2,000 edges per template.");

        var fromNodeId = command.FromNodeId;
        var toNodeId = command.ToNodeId;

        // Resolve group references to first node in group
        if (fromNodeId == 0 && command.FromGroupId is not null)
        {
            var node = template.Nodes.FirstOrDefault(n => n.GroupId == command.FromGroupId);
            if (node is null) return Result.Failure("Source group has no nodes.");
            fromNodeId = node.Id;
        }

        if (toNodeId == 0 && command.ToGroupId is not null)
        {
            var node = template.Nodes.FirstOrDefault(n => n.GroupId == command.ToGroupId);
            if (node is null) return Result.Failure("Target group has no nodes.");
            toNodeId = node.Id;
        }

        if (fromNodeId == toNodeId)
            return Result.Failure("Cannot create an edge from a node to itself.");

        template.AddEdge(fromNodeId, toNodeId);
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
