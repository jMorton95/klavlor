using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UpdateGroupPosition;

public sealed class UpdateGroupPositionHandler(ITemplateRepository templateRepository)
{
    public async Task<Result> Handle(UpdateGroupPositionCommand command, int userId)
    {
        var ownerId = await templateRepository.GetTemplateOwnerId(command.TemplateId);

        if (ownerId is null)
            return Result.Failure("Template not found.");

        if (ownerId != userId)
            return Result.Failure("You do not have permission to modify this template.");

        await templateRepository.UpdateGroupPosition(command.GroupId, command.PositionX, command.PositionY);

        return Result.Success();
    }
}
