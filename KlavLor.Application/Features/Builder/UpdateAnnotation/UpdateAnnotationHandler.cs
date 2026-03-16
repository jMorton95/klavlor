using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UpdateAnnotation;

public sealed class UpdateAnnotationHandler(
    ITemplateRepository templateRepository,
    UpdateAnnotationValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(UpdateAnnotationCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("You do not have permission to modify this template.");

        template.UpdateAnnotation(command.AnnotationId, command.Text, command.FontSize);
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
