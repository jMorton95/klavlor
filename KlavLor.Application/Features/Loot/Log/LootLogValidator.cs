using FluentValidation;

namespace KlavLor.Application.Features.Loot.Log;

public sealed class LootLogValidator : AbstractValidator<LootLogQuery>
{
    public LootLogValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SearchTerm).MaximumLength(100);
    }
}
