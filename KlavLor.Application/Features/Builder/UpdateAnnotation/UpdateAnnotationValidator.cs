using FluentValidation;

namespace KlavLor.Application.Features.Builder.UpdateAnnotation;

public sealed class UpdateAnnotationValidator : AbstractValidator<UpdateAnnotationCommand>
{
    private static readonly HashSet<string> ValidFontSizes = ["small", "medium", "large"];

    public UpdateAnnotationValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.AnnotationId).GreaterThan(0);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(500);
        RuleFor(x => x.FontSize).Must(v => ValidFontSizes.Contains(v))
            .WithMessage("Font size must be small, medium, or large.");
    }
}
