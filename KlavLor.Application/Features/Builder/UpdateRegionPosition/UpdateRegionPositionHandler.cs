using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UpdateRegionPosition;

public sealed class UpdateRegionPositionHandler(
    ITemplateRepository templateRepository,
    UpdateRegionPositionValidator validator)
{
    public async Task<Result> Handle(UpdateRegionPositionCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var ownerId = await templateRepository.GetTemplateOwnerId(command.TemplateId);
        if (ownerId is null)
            return Result.Failure("Template not found.");

        if (ownerId != userId)
            return Result.Failure("You do not have permission to modify this template.");

        await templateRepository.UpdateRegionPosition(command.RegionId, command.PositionX, command.PositionY);
        return Result.Success();
    }
}
