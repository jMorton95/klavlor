using FluentValidation;

namespace KlavLor.Application.Features.Templates.Create;

public sealed class TemplateCreateValidator : AbstractValidator<TemplateCreateCommand>
{
    public TemplateCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Template name is required and must be 100 characters or less.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
