using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UpdateNode;

public sealed class UpdateNodeHandler(
    ITemplateRepository templateRepository,
    UpdateNodeValidator validator)
{
    public async Task<Result> Handle(UpdateNodeCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result.ValidationFailure(validationResult.ToDictionary());

        var template = await templateRepository.GetById(command.TemplateId);

        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result.Failure("You do not have permission to modify this template.");

        var node = template.Nodes.SingleOrDefault(n => n.Id == command.NodeId);

        if (node is null)
            return Result.Failure("Node not found.");

        node.Label = command.Label;
        node.NodeType = (NodeType)command.NodeType;
        node.IconUrl = command.IconUrl;
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
