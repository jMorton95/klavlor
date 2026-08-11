using KlavLor.Application.Features.ApiKeys.Generate;
using KlavLor.Application.Features.ApiKeys.Search;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Users.Commands;

public sealed class ApiKeyManageEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.UserApiKeySection.FromApi(), GetSection)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.UserApiKeyGenerate.FromApi(), Generate)
            .RequireAuthorization(nameof(RoleName.Admin))
            .DisableAntiforgery()
            .RequireRateLimiting("mutation");

        return app.MapPost(AppRoutes.UserApiKeyRevoke.FromApi(), Revoke)
            .RequireAuthorization(nameof(RoleName.Admin))
            .DisableAntiforgery()
            .RequireRateLimiting("mutation");
    }

    private static async Task<RazorComponentResult> GetSection(
        int id,
        ApiKeySearchHandler searchHandler)
    {
        var activeKey = await GetActiveKey(id, searchHandler);
        return IResultExtensions.Component<ApiKeySection>(new { UserId = id, ActiveKey = activeKey });
    }

    private static async Task<RazorComponentResult> Generate(
        int id,
        ApiKeyGenerateHandler generateHandler,
        ApiKeySearchHandler searchHandler)
    {
        var result = await generateHandler.Handle(id);

        if (!result.IsSuccess)
            return IResultExtensions.Component<ApiKeySection>(new { UserId = id, ErrorMessage = result.ErrorMessage });

        var activeKey = await GetActiveKey(id, searchHandler);
        return IResultExtensions.Component<ApiKeySection>(new { UserId = id, ActiveKey = activeKey, GeneratedKey = result.Value!.Key });
    }

    private static async Task<RazorComponentResult> Revoke(
        int id,
        IApiKeyRepository apiKeyRepository,
        ApiKeySearchHandler searchHandler)
    {
        await apiKeyRepository.DeactivateAllForUser(id);
        return IResultExtensions.Component<ApiKeySection>(new { UserId = id });
    }

    private static async Task<ApiKeySearchResponse?> GetActiveKey(int userId, ApiKeySearchHandler searchHandler)
    {
        var keys = await searchHandler.Handle(userId);
        return keys.FirstOrDefault(k => k.IsActive);
    }
}
