namespace KlavLor.Web.Application;

public static class RouteExtensions
{
    extension(string route)
    {
        public string FromApi() => $"/api{route}";
        public string WithId(int id) => route.Replace("{id:int}", id.ToString());
        public string WithDate(DateOnly date) => route.Replace("{date}", date.ToString("yyyy-MM-dd"));

        public string WithQueryParameters(string queryParameters) => $"{route}?{queryParameters}";
    }
}

public static class AppRoutes
{
    public const string Home = "/";

    public const string Login = "/login";
    public const string LoginModal = "/login/modal";
    public const string Logout = "/logout";

    public const string SidebarAccount = "/account/sidebar";

    public const string UsersSearch = "/admin/users";
    public const string UsersCreate = "/admin/users/create";
    public const string UsersEdit = "/admin/users/edit/{id:int}";
    public const string UsersDelete = "/admin/users/delete/{id:int}";

    public const string UserRolesSection = "/admin/users/{id:int}/roles";
    public const string UserRoleToggle = "/admin/users/{id:int}/roles/toggle";

    public const string SyncLog = "/admin/sync-log";

    public const string TemplatesSearch = "/templates";
    public const string TemplatesCreate = "/templates/create";
    public const string TemplatesEdit = "/templates/edit/{id:int}";
    public const string TemplatesDelete = "/templates/delete/{id:int}";
    public const string TemplatesView = "/templates/{id:int}";

    public const string TemplatesDuplicate = "/templates/{id:int}/duplicate";
    public const string TemplatesExport = "/templates/{id:int}/export";

    public const string Builder = "/templates/{id:int}/builder";
    public const string BuilderNodes = "/templates/{id:int}/builder/nodes";
    public const string BuilderNodeCreate = "/templates/{id:int}/builder/nodes/create";
    public const string BuilderNode = "/templates/{id:int}/builder/nodes/{nodeId:int}";
    public const string BuilderNodeEdit = "/templates/{id:int}/builder/nodes/{nodeId:int}/edit";
    public const string BuilderNodePosition = "/templates/{id:int}/builder/nodes/{nodeId:int}/position";
    public const string BuilderNodeReorder = "/templates/{id:int}/builder/nodes/{nodeId:int}/reorder";
    public const string BuilderEdges = "/templates/{id:int}/builder/edges";
    public const string BuilderEdge = "/templates/{id:int}/builder/edges/{edgeId:int}";

    public const string BuilderGroups = "/templates/{id:int}/builder/groups";
    public const string BuilderGroup = "/templates/{id:int}/builder/groups/{groupId:int}";
    public const string BuilderGroupPosition = "/templates/{id:int}/builder/groups/{groupId:int}/position";
    public const string BuilderNodeGroup = "/templates/{id:int}/builder/nodes/{nodeId:int}/group";

    public const string BuilderAutoLayout = "/templates/{id:int}/builder/auto-layout";
    public const string BuilderUndoLayout = "/templates/{id:int}/builder/undo-layout";

    public const string BuilderAnnotations = "/templates/{id:int}/builder/annotations";
    public const string BuilderAnnotationCreate = "/templates/{id:int}/builder/annotations/create";
    public const string BuilderAnnotation = "/templates/{id:int}/builder/annotations/{annotationId:int}";
    public const string BuilderAnnotationEdit = "/templates/{id:int}/builder/annotations/{annotationId:int}/edit";
    public const string BuilderAnnotationPosition = "/templates/{id:int}/builder/annotations/{annotationId:int}/position";

    public const string BuilderRegions = "/templates/{id:int}/builder/regions";
    public const string BuilderRegion = "/templates/{id:int}/builder/regions/{regionId:int}";
    public const string BuilderRegionEdit = "/templates/{id:int}/builder/regions/{regionId:int}/edit";
    public const string BuilderRegionPosition = "/templates/{id:int}/builder/regions/{regionId:int}/position";
    public const string BuilderRegionSize = "/templates/{id:int}/builder/regions/{regionId:int}/size";

    public const string ViewerCompletion = "/templates/{id:int}/completion/{nodeId:int}";

    public const string OsrsSearch = "/osrs/search";
    public const string CachedImage = "/images/{imageId:int}";
    public const string ItemIcon = "/images/item-icon";
    public const string SourceIcon = "/images/source-icon";

    public const string LootIngest = "/loot/ingest";
    public const string LootIngestBatch = "/loot/ingest/batch";
    public const string LootFeed = "/loot/feed";
    public const string LootFeedGrid = "/loot/feed/grid";
    public const string LootFeedStreamStandard = "/loot/feed/stream/standard";
    public const string LootFeedStreamUncommon = "/loot/feed/stream/uncommon";
    public const string LootFeedStreamRare = "/loot/feed/stream/rare";
    public const string LootFeedStreamEpic = "/loot/feed/stream/epic";
    public const string LootFeedStreamLegendary = "/loot/feed/stream/legendary";
    public const string LootFeedLeagues = "/loot/feed/leagues";
    public const string LootFeedLeaguesGrid = "/loot/feed/leagues/grid";
    public const string LootFeedLeaguesStreamStandard = "/loot/feed/leagues/stream/standard";
    public const string LootFeedLeaguesStreamUncommon = "/loot/feed/leagues/stream/uncommon";
    public const string LootFeedLeaguesStreamRare = "/loot/feed/leagues/stream/rare";
    public const string LootFeedLeaguesStreamEpic = "/loot/feed/leagues/stream/epic";
    public const string LootFeedLeaguesStreamLegendary = "/loot/feed/leagues/stream/legendary";
    public const string LootFeedSourcePopover = "/loot/feed/source-popover/{id:int}";
    public const string LootLog = "/loot/log";
    public const string LootLogCharacter = "/loot/log/{id:int}";
    public const string LootLogSource = "/loot/log/{id:int}/source";

    public const string LootLogCharacterHeatmap = "/loot/log/{id:int}/stats/heatmap";
    public const string LootLogCharacterMonthly = "/loot/log/{id:int}/stats/monthly";
    public const string LootLogCharacterRecords = "/loot/log/{id:int}/stats/records";
    public const string LootLogCharacterTopItems = "/loot/log/{id:int}/stats/top-items";
    public const string LootLogCharacterRecentFirsts = "/loot/log/{id:int}/stats/recent-firsts";
    public const string LootLogCharacterSessions = "/loot/log/{id:int}/sessions";
    public const string LootLogCharacterFirsts = "/loot/log/{id:int}/records";
    public const string LootLogCharacterDay = "/loot/log/{id:int}/day/{date}";
    public const string LootLogSourceCollection = "/loot/log/{id:int}/source/collection";
    public const string LootLogSourceSession = "/loot/log/{id:int}/source/session";
    public const string LootLogCharacterSources = "/loot/log/{id:int}/sources";

    public const string UserApiKeySection = "/admin/users/{id:int}/api-key";
    public const string UserApiKeyGenerate = "/admin/users/{id:int}/api-key/generate";
    public const string UserApiKeyRevoke = "/admin/users/{id:int}/api-key/revoke";

    public const string Search = "/search";
    public const string SearchSections = "/search/sections";
    public const string SearchSectionCharacters = "/search/sections/characters";
    public const string SearchSectionSources = "/search/sections/sources";
    public const string SearchSectionDrops = "/search/sections/drops";
    public const string SearchSectionItems = "/search/sections/items";
    public const string SearchSectionTemplates = "/search/sections/templates";
    public const string SearchSectionUsers = "/search/sections/users";

    public const string Source = "/source";
    public const string SourcePlayers = "/source/players";
    public const string SourceClogs = "/source/clogs";
    public const string SourceItems = "/source/items";
    public const string SourceTrend = "/source/trend";

    public const string Drop = "/drop";
    public const string DropSources = "/drop/sources";
    public const string DropCharacters = "/drop/characters";
    public const string DropTrend = "/drop/trend";
    public const string DropSessions = "/drop/sessions";

    public const string Characters = "/characters";
    public const string CharacterUpdateName = "/characters/{id:int}/name";
    public const string CharacterToggleVisibility = "/characters/{id:int}/visibility";
    public const string CharacterToggleLeagues = "/characters/{id:int}/leagues";

    public const string AdminCharacterSection = "/admin/users/{id:int}/characters";
    public const string AdminCharacterToggleHidden = "/admin/users/{id:int}/characters/{characterId:int}/hidden";
    public const string AdminCharacterToggleLeagues = "/admin/users/{id:int}/characters/{characterId:int}/leagues";
    public const string AdminCharacterToggleVisibility = "/admin/users/{id:int}/characters/{characterId:int}/visibility";
    public const string AdminCharacterUpdateName = "/admin/users/{id:int}/characters/{characterId:int}/name";
    public const string AdminCharacterDelete = "/admin/users/{id:int}/characters/{characterId:int}/delete";
    public const string AdminUserDeleteLoot = "/admin/users/{id:int}/delete-loot";
    public const string AdminCharacterAssign = "/characters/{id:int}/assign";

    public const string HealthCheck = "/healthcheck";

    public const string AdminSettings = "/admin/settings";
    public const string AdminSettingsLeaguesToggle = "/admin/settings/leagues/toggle";
    public const string AdminClogSearch = "/admin/settings/collection-log/search";
    public const string AdminClogExclude = "/admin/settings/collection-log/exclude";
    public const string AdminClogInclude = "/admin/settings/collection-log/include";
    public const string AdminDropRatesSearch = "/admin/settings/drop-rates/search";
    public const string AdminDropRatesSync = "/admin/settings/drop-rates/sync";
    public const string AdminDropRatesMismatches = "/admin/settings/drop-rates/mismatches";
    public const string AdminIcons = "/admin/settings/icons";
    public const string AdminIconsRetry = "/admin/settings/icons/retry";
    public const string AdminSyncStatus = "/admin/settings/sync-status";
    public const string AdminClogSyncNow = "/admin/settings/collection-log/sync-now";
    public const string AdminSourceSearch = "/admin/settings/sources/search";
    public const string AdminSourceRenamePreview = "/admin/settings/sources/rename/preview";
    public const string AdminSourceRename = "/admin/settings/sources/rename";
    public const string AdminSourceRow = "/admin/settings/sources/row";
}
