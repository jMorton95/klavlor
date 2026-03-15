using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.AddNode;

public sealed class AddNodeHandler(
    ITemplateRepository templateRepository,
    AddNodeValidator validator)
{
    public async Task<Result<TemplateNode>> Handle(AddNodeCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result<TemplateNode>.ValidationFailure(validationResult.ToDictionary());

        var template = await templateRepository.GetById(command.TemplateId);

        if (template is null)
            return Result<TemplateNode>.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result<TemplateNode>.Failure("You do not have permission to modify this template.");

        if (template.Nodes.Count >= 500)
            return Result<TemplateNode>.Failure("Maximum of 500 nodes per template.");

        TemplateNode node;
        if (command.GroupId.HasValue)
        {
            node = template.AddNode(command.Label, (NodeType)command.NodeType, command.PositionX, command.PositionY, iconUrl: command.IconUrl, groupId: command.GroupId, color: command.Color);
        }
        else
        {
            (_, node) = template.AddNodeToNewGroup(command.Label, (NodeType)command.NodeType, command.PositionX, command.PositionY, iconUrl: command.IconUrl, color: command.Color);
        }

        await templateRepository.SaveTemplate(template);

        return Result<TemplateNode>.Success(node);
    }
}
