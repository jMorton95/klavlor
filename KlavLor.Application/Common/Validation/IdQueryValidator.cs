using FluentValidation;

namespace KlavLor.Application.Common.Validation;

public abstract class IdQueryValidator : AbstractValidator<IdRecord>
{
    protected IdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .GreaterThan(0);
    }
}
