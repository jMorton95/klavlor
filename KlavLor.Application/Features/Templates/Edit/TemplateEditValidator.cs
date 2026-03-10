using FluentValidation;

namespace KlavLor.Application.Features.Templates.Edit;

public sealed class TemplateEditValidator : AbstractValidator<TemplateEditCommand>
{
    public TemplateEditValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
