using FluentValidation;

namespace KlavLor.Application.Common.Validation;

public abstract class PagedQueryValidator<T> : AbstractValidator<PagedQuery>
{
    protected PagedQueryValidator()
    {
        When(x => !string.IsNullOrWhiteSpace(x.SortBy), () =>
        {
            RuleFor(x => x.SortBy)
                .Must(sortBy => ValidationExtensions.IsValidPropertyName<T>(sortBy!))
                .WithMessage($"SortBy must be a valid property on {typeof(T).Name}.");
        });

        RuleFor(x => x.SortDirection)
            .Must(x => SortDirection.Ascending.Equals(x) || SortDirection.Descending.Equals(x))
            .WithMessage("SortDirection must be either Ascending or Descending.");

        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(100)
            .WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(10000)
            .WithMessage("PageNumber must be between 1 and 10000.");

        RuleFor(x => x.SearchTerm)
            .MaximumLength(255);
    }
}
