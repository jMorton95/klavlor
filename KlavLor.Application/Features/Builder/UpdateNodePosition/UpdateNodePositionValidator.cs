using FluentValidation;

namespace KlavLor.Application.Features.Builder.UpdateNodePosition;

public sealed class UpdateNodePositionValidator : AbstractValidator<UpdateNodePositionCommand>
{
    public UpdateNodePositionValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.NodeId).GreaterThan(0);
        RuleFor(x => x.PositionX).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.PositionY).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
    }
}
