namespace KlavLor.Application.Features.ApiKeys.Search;

public sealed class ApiKeySearchResponse
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
