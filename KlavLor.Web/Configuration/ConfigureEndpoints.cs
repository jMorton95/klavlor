using KlavLor.Web.Application;
using KlavLor.Web.Application.Features.HealthCheck;
using KlavLor.Web.Application.Features.Home;
using KlavLor.Web.Application.Features.Login;
using KlavLor.Web.Application.Features.Logout;
using KlavLor.Web.Application.Features.Templates.Builder;
using KlavLor.Web.Application.Features.Templates.Commands;
using KlavLor.Web.Application.Features.Templates.Queries;
using KlavLor.Web.Application.Features.Users.Commands;
using KlavLor.Web.Application.Features.Users.Queries;
using KlavLor.Web.Application.Features.Viewer;

namespace KlavLor.Web.Configuration;

public static class ConfigureEndpoints
{
    public static void MapApplicationRequestHandlers(this WebApplication app)
    {
        var web = app.MapGroup("/");

        web.MapEndpoints<LoginEndpoint>()
            .MapEndpoints<LogoutEndpoint>()
            .MapEndpoints<HomePageEndpoint>();

        web.MapEndpoints<UserSearchEndpoint>()
            .MapEndpoints<UserCreateEndpoint>()
            .MapEndpoints<UserEditEndpoint>()
            .MapEndpoints<UserDeleteEndpoint>();

        web.MapEndpoints<TemplateSearchEndpoint>()
            .MapEndpoints<TemplateCreateEndpoint>()
            .MapEndpoints<TemplateEditEndpoint>()
            .MapEndpoints<TemplateDeleteEndpoint>();

        web.MapEndpoints<BuilderEndpoint>()
            .MapEndpoints<NodeEndpoints>()
            .MapEndpoints<EdgeEndpoints>()
            .MapEndpoints<GroupEndpoints>()
            .MapEndpoints<OsrsSearchEndpoint>();

        web.MapEndpoints<ViewerEndpoint>()
            .MapEndpoints<ShareEndpoint>()
            .MapEndpoints<CompletionEndpoint>();

        web.MapEndpoints<HealthCheckEndpoint>();
    }

    private static IEndpointRouteBuilder MapEndpoints<TEndpointHandler>
        (this IEndpointRouteBuilder app) where TEndpointHandler : IEndpoint
    {
        TEndpointHandler.MapEndpoint(app);

        return app;
    }
}
