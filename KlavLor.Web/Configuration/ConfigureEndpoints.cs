using KlavLor.Web.Application;
using KlavLor.Web.Application.Features.HealthCheck;
using KlavLor.Web.Application.Features.Home;
using KlavLor.Web.Application.Features.Login;
using KlavLor.Web.Application.Features.Characters;
using KlavLor.Web.Application.Features.Logout;
using KlavLor.Web.Application.Features.Templates.Builder;
using KlavLor.Web.Application.Features.Templates.Commands;
using KlavLor.Web.Application.Features.Templates.Queries;
using KlavLor.Web.Application.Features.Users.Account;
using KlavLor.Web.Application.Features.Users.Commands;
using KlavLor.Web.Application.Features.Users.Queries;
using KlavLor.Web.Application.Features.Loot;
using KlavLor.Web.Application.Features.Loot.Feed;
using KlavLor.Web.Application.Features.Loot.Ingest;
using KlavLor.Web.Application.Features.Loot.Ingest.Audit;
using KlavLor.Web.Application.Features.Loot.Log;
using KlavLor.Web.Application.Features.Loot.Leaderboard;
using KlavLor.Web.Application.Features.Search;
using KlavLor.Web.Application.Features.Source;
using KlavLor.Web.Application.Features.Drop;
using KlavLor.Web.Application.Features.Settings;
using KlavLor.Web.Application.Features.Viewer;

namespace KlavLor.Web.Configuration;

public static class ConfigureEndpoints
{
    public static void MapApplicationRequestHandlers(this WebApplication app)
    {
        var web = app.MapGroup("/");

        web.MapEndpoints<LoginEndpoint>()
            .MapEndpoints<LoginModalEndpoint>()
            .MapEndpoints<LogoutEndpoint>()
            .MapEndpoints<HomePageEndpoint>();

        web.MapEndpoints<UserSearchEndpoint>()
            .MapEndpoints<UserCreateEndpoint>()
            .MapEndpoints<UserEditEndpoint>()
            .MapEndpoints<UserDeleteEndpoint>()
            .MapEndpoints<UserRolesEndpoint>()
            .MapEndpoints<SidebarAccountEndpoint>();

        web.MapEndpoints<TemplateSearchEndpoint>()
            .MapEndpoints<TemplateCreateEndpoint>()
            .MapEndpoints<TemplateEditEndpoint>()
            .MapEndpoints<TemplateDeleteEndpoint>();

        web.MapEndpoints<BuilderEndpoint>()
            .MapEndpoints<NodeEndpoints>()
            .MapEndpoints<EdgeEndpoints>()
            .MapEndpoints<GroupEndpoints>()
            .MapEndpoints<LayoutEndpoints>()
            .MapEndpoints<AnnotationEndpoints>()
            .MapEndpoints<RegionEndpoints>()
            .MapEndpoints<OsrsSearchEndpoint>()
            .MapEndpoints<ImageEndpoint>();

        web.MapEndpoints<ViewerEndpoint>()
            .MapEndpoints<CompletionEndpoint>();

        web.MapEndpoints<LootIngestEndpoint>()
            .MapEndpoints<LootFeedEndpoint>()
            .MapEndpoints<SourcePopoverEndpoint>()
            .MapEndpoints<LootLogEndpoint>()
            .MapEndpoints<LootCharacterProfileEndpoint>()
            .MapEndpoints<LuckLeaderboardEndpoint>()
            .MapEndpoints<SyncLogEndpoint>()
            .MapEndpoints<ItemIconEndpoint>()
            .MapEndpoints<SourceIconEndpoint>();

        web.MapEndpoints<ApiKeyManageEndpoint>()
            .MapEndpoints<AdminCharacterEndpoint>()
            .MapEndpoints<AdminSettingsEndpoint>();

        web.MapEndpoints<CharacterEndpoint>();

        web.MapEndpoints<SearchEndpoint>()
            .MapEndpoints<SourceEndpoint>()
            .MapEndpoints<DropEndpoint>();

        web.MapEndpoints<HealthCheckEndpoint>();
    }

    private static IEndpointRouteBuilder MapEndpoints<TEndpointHandler>
        (this IEndpointRouteBuilder app) where TEndpointHandler : IEndpoint
    {
        TEndpointHandler.MapEndpoint(app);

        return app;
    }
}
