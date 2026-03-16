using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UpdateGroupPosition;

public sealed class UpdateGroupPositionHandler(
    ITemplateRepository templateRepository,
    UpdateGroupPositionValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(UpdateGroupPositionCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var ownerId = await templateRepository.GetTemplateOwnerId(command.TemplateId);

        if (ownerId is null)
            return Result.Failure("Template not found.");

        if (ownerId != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("You do not have permission to modify this template.");

        await templateRepository.UpdateGroupPosition(command.GroupId, command.PositionX, command.PositionY);

        return Result.Success();
    }
}
