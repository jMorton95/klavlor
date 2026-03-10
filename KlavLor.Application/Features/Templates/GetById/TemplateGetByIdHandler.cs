using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.GetById;

public sealed class TemplateGetByIdHandler(
    ITemplateRepository templateRepository,
    TemplateGetByIdValidator validator)
{
    public async Task<Result<TemplateResponse>> Handle(TemplateGetByIdQuery query)
    {
        var validationResult = await validator.ValidateAsync(query);

        if (!validationResult.IsValid)
            return Result<TemplateResponse>.ValidationFailure(validationResult.ToDictionary());

        var template = await templateRepository.GetById(query.Id!.Value);

        if (template is null)
            return Result<TemplateResponse>.Failure("Template not found.");

        return Result<TemplateResponse>.Success(template.MapToResponse());
    }
}
