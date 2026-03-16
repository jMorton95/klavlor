using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UpdateRegion;

public sealed class UpdateRegionHandler(
    ITemplateRepository templateRepository,
    UpdateRegionValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(UpdateRegionCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("You do not have permission to modify this template.");

        template.UpdateRegion(command.RegionId, command.Label, command.Color, command.Opacity);
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
