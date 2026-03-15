using FluentValidation;

namespace KlavLor.Application.Features.Builder.UpdateRegion;

public sealed class UpdateRegionValidator : AbstractValidator<UpdateRegionCommand>
{
    public UpdateRegionValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.RegionId).GreaterThan(0);
        RuleFor(x => x.Label).MaximumLength(100);
        RuleFor(x => x.Color).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Opacity).InclusiveBetween(0.05, 0.5);
    }
}
