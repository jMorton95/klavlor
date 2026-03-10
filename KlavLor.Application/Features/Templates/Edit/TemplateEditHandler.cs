using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.Edit;

public sealed class TemplateEditHandler(
    ITemplateRepository templateRepository,
    TemplateEditValidator validator)
{
    public async Task<Result<Template>> Handle(TemplateEditCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result<Template>.ValidationFailure(validationResult.ToDictionary());

        var template = await templateRepository.GetById(command.Id);

        if (template is null)
            return Result<Template>.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result<Template>.Failure("You do not have permission to edit this template.");

        template.UpdateDetails(command.Name, command.Description, command.IsPublic);
        await templateRepository.SaveTemplate(template);

        return Result<Template>.Success(template);
    }
}
