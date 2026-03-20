using FluentValidation;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Ingest;

public sealed class LootIngestValidator : AbstractValidator<LootIngestCommand>
{
    private static readonly HashSet<string> ValidTypes =
        Enum.GetNames<LootSourceType>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    public LootIngestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).NotEmpty()
            .Must(t => ValidTypes.Contains(t))
            .WithMessage("Type must be a valid loot source type.");
        RuleFor(x => x.Drops).NotEmpty();
        RuleFor(x => x.Date).NotEmpty();
    }
}
