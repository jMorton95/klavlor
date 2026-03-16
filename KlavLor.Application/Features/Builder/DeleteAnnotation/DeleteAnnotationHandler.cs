using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.DeleteAnnotation;

public sealed class DeleteAnnotationHandler(
    ITemplateRepository templateRepository,
    DeleteAnnotationValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(DeleteAnnotationCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("You do not have permission to modify this template.");

        template.RemoveAnnotation(command.AnnotationId);
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }
}
