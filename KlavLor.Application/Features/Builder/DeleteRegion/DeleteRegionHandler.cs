using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.DeleteRegion;

public sealed class DeleteRegionHandler(
    ITemplateRepository templateRepository,
    DeleteRegionValidator validator)
{
    public async Task<Result> Handle(DeleteRegionCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result.Failure("You do not have permission to modify this template.");

        template.RemoveRegion(command.RegionId);
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
