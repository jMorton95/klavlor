using FluentValidation;

namespace KlavLor.Application.Features.Builder.AddAnnotation;

public sealed class AddAnnotationValidator : AbstractValidator<AddAnnotationCommand>
{
    private static readonly HashSet<string> ValidFontSizes = ["small", "medium", "large"];

    public AddAnnotationValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(500);
        RuleFor(x => x.PositionX).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.PositionY).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.FontSize).Must(v => ValidFontSizes.Contains(v))
            .WithMessage("Font size must be small, medium, or large.");
    }
}
