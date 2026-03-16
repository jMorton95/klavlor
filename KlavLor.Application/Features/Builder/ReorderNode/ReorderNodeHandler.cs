using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.ReorderNode;

public sealed class ReorderNodeHandler(
    ITemplateRepository templateRepository,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(ReorderNodeCommand command)
    {
        var template = await templateRepository.GetById(command.TemplateId);

        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
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
