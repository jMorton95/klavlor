using KlavLor.Application.Common;
using KlavLor.Application.Features.Users.Roles;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Users.Commands;

public sealed class UserRolesEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.UserRolesSection.FromApi(), GetRolesSection)
            .RequireAuthorization(nameof(RoleName.Admin));

        return app.MapPost(AppRoutes.UserRoleToggle.FromApi(), ToggleRole)
            .RequireAuthorization(nameof(RoleName.Admin));
    }

    private static async Task<RazorComponentResult> GetRolesSection(int id, UserRolesHandler handler)
    {
        var result = await handler.Handle(id);
        return IResultExtensions.Component<UserRolesSection>(new { UserId = id, Roles = result.Value });
    }

    private static async Task<RazorComponentResult> ToggleRole(int id, string role, UserRolesHandler handler)
    {
        var toggle = Enum.TryParse<RoleName>(role, ignoreCase: true, out var parsed)
            ? await handler.Toggle(id, parsed)
            : Result<UserRolesResponse>.Failure("Unknown role.");

        // Re-render with the current role state even on failure so the section stays consistent.
        var roles = toggle.IsSuccess ? toggle.Value : (await handler.Handle(id)).Value;

        return IResultExtensions.Component<UserRolesSection>(new
        {
            UserId = id,
            Roles = roles,
            ErrorMessage = toggle.IsSuccess ? null : toggle.ErrorMessage
        });
    }
}
