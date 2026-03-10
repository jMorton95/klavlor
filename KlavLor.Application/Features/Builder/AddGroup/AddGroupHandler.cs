using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.AddGroup;

public sealed class AddGroupHandler(ITemplateRepository templateRepository)
{
    public async Task<Result<TemplateNodeGroup>> Handle(AddGroupCommand command, int userId)
    {
        var template = await templateRepository.GetById(command.TemplateId);

        if (template is null)
            return Result<TemplateNodeGroup>.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result<TemplateNodeGroup>.Failure("You do not have permission to modify this template.");

        var group = template.AddGroup(command.PositionX, command.PositionY);
        await templateRepository.SaveTemplate(template);

        return Result<TemplateNodeGroup>.Success(group);
    }
}
