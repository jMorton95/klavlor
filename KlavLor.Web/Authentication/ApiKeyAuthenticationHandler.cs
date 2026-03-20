using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using System.Security.Cryptography;

namespace KlavLor.Web.Authentication;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyRepository apiKeyRepository)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var key = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(key))
            return AuthenticateResult.NoResult();

        var keyHash = HashKey(key);
        var apiKey = await apiKeyRepository.GetByKeyHash(keyHash);
        if (apiKey is null)
            return AuthenticateResult.Fail("Invalid API key.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, apiKey.UserId.ToString()),
            new(ClaimTypes.AuthenticationMethod, SchemeName)
        };

        // Add the user's roles
        if (apiKey.User is { UserRoles: not null } user)
        {
            foreach (var userRole in user.UserRoles)
            {
                if (userRole.Role is not null)
                    claims.Add(new Claim(ClaimTypes.Role, userRole.Role.Name.ToString()));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        await apiKeyRepository.UpdateLastUsedAt(apiKey.Id, DateTimeOffset.UtcNow);

        return AuthenticateResult.Success(ticket);
    }

    private static string HashKey(string plainTextKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plainTextKey));
        return Convert.ToHexStringLower(hash);
    }
}
