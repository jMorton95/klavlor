using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UpdateAnnotationPosition;

public sealed class UpdateAnnotationPositionHandler(
    ITemplateRepository templateRepository,
    UpdateAnnotationPositionValidator validator)
{
    public async Task<Result> Handle(UpdateAnnotationPositionCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var ownerId = await templateRepository.GetTemplateOwnerId(command.TemplateId);
        if (ownerId is null)
            return Result.Failure("Template not found.");

        if (ownerId != userId)
            return Result.Failure("You do not have permission to modify this template.");

        await templateRepository.UpdateAnnotationPosition(command.AnnotationId, command.PositionX, command.PositionY);
        return Result.Success();
    }
}
