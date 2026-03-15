using FluentValidation;

namespace KlavLor.Application.Features.Builder.UpdateRegionSize;

public sealed class UpdateRegionSizeValidator : AbstractValidator<UpdateRegionSizeCommand>
{
    public UpdateRegionSizeValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.RegionId).GreaterThan(0);
        RuleFor(x => x.Width).InclusiveBetween(50, 10000);
        RuleFor(x => x.Height).InclusiveBetween(50, 10000);
    }
}
