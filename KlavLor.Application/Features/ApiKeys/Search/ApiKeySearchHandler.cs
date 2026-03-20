using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.ApiKeys.Search;

public sealed class ApiKeySearchHandler(IApiKeyRepository apiKeyRepository)
{
    public async Task<List<ApiKeySearchResponse>> Handle(int? userId = null)
    {
        var keys = userId.HasValue
            ? await apiKeyRepository.GetByUserId(userId.Value)
            : await apiKeyRepository.GetAll();

        return keys.Select(k => new ApiKeySearchResponse
        {
            Id = k.Id,
            UserId = k.UserId,
            UserName = k.User is not null ? $"{k.User.FirstName} {k.User.LastName}" : "",
            KeyPrefix = k.KeyPrefix,
            Name = k.Name,
            IsActive = k.IsActive,
            LastUsedAt = k.LastUsedAt,
            CreatedAt = k.CreatedAt
        }).ToList();
    }
}
