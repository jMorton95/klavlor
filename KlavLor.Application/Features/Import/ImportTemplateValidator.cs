using FluentValidation;

namespace KlavLor.Application.Features.Import;

public sealed class ImportTemplateValidator : AbstractValidator<ImportTemplateCommand>
{
    public ImportTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Template name is required and must be 100 characters or less.");
        RuleFor(x => x.JsonData).NotEmpty()
            .WithMessage("Progression data is required.");
    }
}
