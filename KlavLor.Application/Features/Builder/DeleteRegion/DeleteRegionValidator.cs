using FluentValidation;

namespace KlavLor.Application.Features.Builder.DeleteRegion;

public sealed class DeleteRegionValidator : AbstractValidator<DeleteRegionCommand>
{
    public DeleteRegionValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.RegionId).GreaterThan(0);
    }
}
