using FluentValidation;

namespace KlavLor.Application.Features.Builder.UpdateAnnotationPosition;

public sealed class UpdateAnnotationPositionValidator : AbstractValidator<UpdateAnnotationPositionCommand>
{
    public UpdateAnnotationPositionValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.AnnotationId).GreaterThan(0);
        RuleFor(x => x.PositionX).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.PositionY).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
    }
}
