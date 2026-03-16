using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.AddAnnotation;

public sealed class AddAnnotationHandler(
    ITemplateRepository templateRepository,
    AddAnnotationValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result<CanvasAnnotation>> Handle(AddAnnotationCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result<CanvasAnnotation>.Failure("Validation failed.");

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result<CanvasAnnotation>.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result<CanvasAnnotation>.Failure("You do not have permission to modify this template.");

        var annotation = template.AddAnnotation(command.Text, command.PositionX, command.PositionY, command.FontSize);
        await templateRepository.SaveTemplate(template);

        return Result<CanvasAnnotation>.Success(annotation);
    }
}
