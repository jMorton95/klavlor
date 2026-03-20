using System.Security.Cryptography;
using System.Text;
using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.ApiKeys.Create;

public sealed class ApiKeyCreateHandler(
    IApiKeyRepository apiKeyRepository,
    ApiKeyCreateValidator validator)
{
    public async Task<Result<ApiKeyCreateResult>> Handle(ApiKeyCreateCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result<ApiKeyCreateResult>.Failure("Validation failed.");

        var plainTextKey = GenerateApiKey();
        var keyHash = HashKey(plainTextKey);
        var keyPrefix = plainTextKey[..8];

        var apiKey = new ApiKey
        {
            UserId = command.UserId,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Name = command.Name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await apiKeyRepository.Save(apiKey);

        return Result<ApiKeyCreateResult>.Success(new ApiKeyCreateResult
        {
            Id = apiKey.Id,
            Key = plainTextKey,
            KeyPrefix = keyPrefix,
            Name = command.Name
        });
    }

    private static string GenerateApiKey()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return "klav_" + RandomNumberGenerator.GetString(chars, 48);
    }

    internal static string HashKey(string plainTextKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plainTextKey));
        return Convert.ToHexStringLower(hash);
    }
}

public sealed class ApiKeyCreateResult
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string Name { get; set; } = "";
}
