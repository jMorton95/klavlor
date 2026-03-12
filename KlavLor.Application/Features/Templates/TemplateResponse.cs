namespace KlavLor.Application.Features.Templates;

public sealed record TemplateResponse(
    int Id,
    string Name,
    string? Description,
    bool IsPublic,
    string CreatedByName,
    int NodeCount,
    int EdgeCount,
    DateTimeOffset SavedAt
);
