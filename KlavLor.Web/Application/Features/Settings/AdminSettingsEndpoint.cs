using KlavLor.Application.Features.Settings;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Settings;

public sealed class AdminSettingsEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapPost(AppRoutes.AdminSettingsLeaguesToggle.FromApi(), ToggleLeagues)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");
    }

    private static async Task<RazorComponentResult> ToggleLeagues(
        SystemSettingsHandler handler,
        ISystemSettingsCache cache)
    {
        await handler.HandleToggleLeagues();
        return IResultExtensions.Component<AdminSettingsToggleLeagues>(new
        {
            IsLeaguesEnabled = cache.IsLeaguesEnabled
        });
    }
}
