namespace KlavLor.Web.Application;

public static class RouteExtensions
{
    extension(string route)
    {
        public string FromApi() => $"/api{route}";
        public string WithId(int id) => route.Replace("{id:int}", id.ToString());
        public string WithToken(string token) => route.Replace("{token}", token);
        public string WithQueryParameters(string queryParameters) => $"{route}?{queryParameters}";
    }
}

public static class AppRoutes
{
    public const string Home = "/";

    public const string Login = "/login";
    public const string Logout = "/logout";

    public const string UsersSearch = "/admin/users";
    public const string UsersCreate = "/admin/users/create";
    public const string UsersEdit = "/admin/users/edit/{id:int}";
    public const string UsersDelete = "/admin/users/delete/{id:int}";

    public const string TemplatesSearch = "/templates";
    public const string TemplatesCreate = "/templates/create";
    public const string TemplatesEdit = "/templates/edit/{id:int}";
    public const string TemplatesDelete = "/templates/delete/{id:int}";
    public const string TemplatesView = "/templates/{id:int}";
    public const string TemplatesShare = "/share/{token}";
    public const string TemplatesImport = "/templates/import";
    public const string TemplatesDuplicate = "/templates/{id:int}/duplicate";
    public const string TemplatesExport = "/templates/{id:int}/export";

    public const string Builder = "/templates/{id:int}/builder";
    public const string BuilderNodes = "/templates/{id:int}/builder/nodes";
    public const string BuilderNodeCreate = "/templates/{id:int}/builder/nodes/create";
    public const string BuilderNode = "/templates/{id:int}/builder/nodes/{nodeId:int}";
    public const string BuilderNodeEdit = "/templates/{id:int}/builder/nodes/{nodeId:int}/edit";
    public const string BuilderNodePosition = "/templates/{id:int}/builder/nodes/{nodeId:int}/position";
    public const string BuilderEdges = "/templates/{id:int}/builder/edges";
    public const string BuilderEdge = "/templates/{id:int}/builder/edges/{edgeId:int}";

    public const string BuilderGroups = "/templates/{id:int}/builder/groups";
    public const string BuilderGroup = "/templates/{id:int}/builder/groups/{groupId:int}";
    public const string BuilderGroupPosition = "/templates/{id:int}/builder/groups/{groupId:int}/position";
    public const string BuilderNodeGroup = "/templates/{id:int}/builder/nodes/{nodeId:int}/group";

    public const string ViewerCompletion = "/templates/{id:int}/completion/{nodeId:int}";

    public const string OsrsSearch = "/osrs/search";
    public const string CachedImage = "/images/{imageId:int}";

    public const string HealthCheck = "/healthcheck";
}
