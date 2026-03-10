using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.AssignNodeToGroup;

public sealed class AssignNodeToGroupHandler(ITemplateRepository templateRepository)
{
    public async Task<Result> Handle(AssignNodeToGroupCommand command, int userId)
    {
        var template = await templateRepository.GetById(command.TemplateId);

        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result.Failure("You do not have permission to modify this template.");

        template.AssignNodeToGroup(command.NodeId, command.GroupId);
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
