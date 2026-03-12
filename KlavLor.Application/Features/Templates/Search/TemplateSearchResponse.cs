namespace KlavLor.Application.Features.Templates.Search;

public sealed record TemplateSearchResponse(
    int Id,
    string Name,
    string? Description,
    bool IsPublic,
    int NodeCount,
    DateTimeOffset SavedAt,
    string CreatedByName,
    bool IsOwner
);
