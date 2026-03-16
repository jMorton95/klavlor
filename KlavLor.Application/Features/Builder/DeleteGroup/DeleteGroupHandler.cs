using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.DeleteGroup;

public sealed class DeleteGroupHandler(
    ITemplateRepository templateRepository,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(DeleteGroupCommand command)
    {
        var template = await templateRepository.GetById(command.TemplateId);

        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("You do not have permission to modify this template.");

        template.RemoveGroup(command.GroupId);
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
