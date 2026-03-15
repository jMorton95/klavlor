using FluentValidation;

namespace KlavLor.Application.Features.Builder.UpdateRegionPosition;

public sealed class UpdateRegionPositionValidator : AbstractValidator<UpdateRegionPositionCommand>
{
    public UpdateRegionPositionValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.RegionId).GreaterThan(0);
        RuleFor(x => x.PositionX).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.PositionY).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
    }
}
