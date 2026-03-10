namespace KlavLor.Application.Features.Templates.Search;

public sealed record TemplateSearchResponse(
    int Id,
    string Name,
    string? Description,
    bool IsPublic,
    string ShareToken,
    int NodeCount,
    DateTimeOffset SavedAt,
    string CreatedByName,
    bool IsOwner
);
