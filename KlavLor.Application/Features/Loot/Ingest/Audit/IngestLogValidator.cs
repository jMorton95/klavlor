using FluentValidation;

namespace KlavLor.Application.Features.Loot.Ingest.Audit;

public sealed class IngestLogValidator : AbstractValidator<IngestLogQuery>
{
    public IngestLogValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
