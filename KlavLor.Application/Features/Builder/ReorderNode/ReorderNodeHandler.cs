using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.ReorderNode;

public sealed class ReorderNodeHandler(ITemplateRepository templateRepository)
{
    public async Task<Result> Handle(ReorderNodeCommand command, int userId)
    {
        var template = await templateRepository.GetById(command.TemplateId);

        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result.Failure("You do not have permission to modify this template.");

        if (string.Equals(command.Direction, "up", StringComparison.OrdinalIgnoreCase))
            template.MoveNodeUp(command.NodeId);
        else if (string.Equals(command.Direction, "down", StringComparison.OrdinalIgnoreCase))
            template.MoveNodeDown(command.NodeId);
        else
            return Result.Failure("Invalid direction. Use 'up' or 'down'.");

        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
