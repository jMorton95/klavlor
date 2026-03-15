using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.AddRegion;

public sealed class AddRegionHandler(
    ITemplateRepository templateRepository,
    AddRegionValidator validator)
{
    public async Task<Result<CanvasRegion>> Handle(AddRegionCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result<CanvasRegion>.Failure("Validation failed.");

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result<CanvasRegion>.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result<CanvasRegion>.Failure("You do not have permission to modify this template.");

        var region = template.AddRegion(
            command.PositionX, command.PositionY,
            command.Width, command.Height,
            command.Color, command.Opacity, command.Label);

        await templateRepository.SaveTemplate(template);
        return Result<CanvasRegion>.Success(region);
    }
}
