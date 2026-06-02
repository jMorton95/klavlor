namespace KlavLor.Application.Features.Search;

// Mirrors the site-wide search convention (cf. UserSearchQuery / TemplateSearchQuery):
// the search term arrives as the `searchTerm` query parameter, bound via [AsParameters].
public sealed record SearchQuery(string? SearchTerm = null);
