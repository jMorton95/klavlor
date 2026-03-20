using System.Security.Cryptography;
using System.Text;
using KlavLor.Application.Common;
using KlavLor.Application.Features.ApiKeys.Create;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.ApiKeys.Generate;

public sealed class ApiKeyGenerateHandler(IApiKeyRepository apiKeyRepository)
{
    public async Task<Result<ApiKeyCreateResult>> Handle(int userId)
    {
        await apiKeyRepository.DeactivateAllForUser(userId);

        var plainTextKey = GenerateApiKey();
        var keyHash = ApiKeyCreateHandler.HashKey(plainTextKey);
        var keyPrefix = plainTextKey[..8];

        var apiKey = new ApiKey
        {
            UserId = userId,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Name = "Generated via UI",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await apiKeyRepository.Save(apiKey);

        return Result<ApiKeyCreateResult>.Success(new ApiKeyCreateResult
        {
            Id = apiKey.Id,
            Key = plainTextKey,
            KeyPrefix = keyPrefix,
            Name = apiKey.Name
        });
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(36);
        return "klav_" + Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")[..48];
    }
}
