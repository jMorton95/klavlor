using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UpdateRegionSize;

public sealed class UpdateRegionSizeHandler(
    ITemplateRepository templateRepository,
    UpdateRegionSizeValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(UpdateRegionSizeCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var ownerId = await templateRepository.GetTemplateOwnerId(command.TemplateId);
        if (ownerId is null)
            return Result.Failure("Template not found.");

        if (ownerId != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("You do not have permission to modify this template.");

        await templateRepository.UpdateRegionSize(command.RegionId, command.Width, command.Height);
        return Result.Success();
    }
}
