using FluentValidation;

namespace KlavLor.Application.Features.Builder.UpdateGroupPosition;

public sealed class UpdateGroupPositionValidator : AbstractValidator<UpdateGroupPositionCommand>
{
    public UpdateGroupPositionValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.GroupId).GreaterThan(0);
        RuleFor(x => x.PositionX).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.PositionY).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
    }
}
