using FluentValidation;

namespace KlavLor.Application.Features.Builder.ApplyAutoLayout;

public sealed class ApplyAutoLayoutValidator : AbstractValidator<ApplyAutoLayoutCommand>
{
    public ApplyAutoLayoutValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
    }
}
