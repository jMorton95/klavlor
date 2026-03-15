using FluentValidation;

namespace KlavLor.Application.Features.Builder.AddRegion;

public sealed class AddRegionValidator : AbstractValidator<AddRegionCommand>
{
    public AddRegionValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.Label).MaximumLength(100);
        RuleFor(x => x.PositionX).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.PositionY).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.Width).InclusiveBetween(50, 10000);
        RuleFor(x => x.Height).InclusiveBetween(50, 10000);
        RuleFor(x => x.Color).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Opacity).InclusiveBetween(0.05, 0.5);
    }
}
