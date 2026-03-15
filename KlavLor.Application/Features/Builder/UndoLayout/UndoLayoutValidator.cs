using FluentValidation;

namespace KlavLor.Application.Features.Builder.UndoLayout;

public sealed class UndoLayoutValidator : AbstractValidator<UndoLayoutCommand>
{
    public UndoLayoutValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
    }
}
