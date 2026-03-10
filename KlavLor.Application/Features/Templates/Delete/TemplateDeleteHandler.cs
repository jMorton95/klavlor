using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.Delete;

public sealed class TemplateDeleteHandler(
    ITemplateRepository templateRepository,
    TemplateDeleteValidator validator)
{
    public async Task<Result> Handle(TemplateDeleteCommand command, int userId)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result.ValidationFailure(validationResult.ToDictionary());

        var template = await templateRepository.GetById(command.Id!.Value);

        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result.Failure("You do not have permission to delete this template.");

        var result = await templateRepository.DeleteTemplate(command.Id!.Value);

        return result > 0
            ? Result.Success()
            : Result.Failure("Failed to delete template.");
    }
}
