namespace KlavLor.Web.Application.Filters;

public sealed class SyncVersionFilter : IEndpointFilter
{
    private const int MinimumSyncVersion = 2;
    private const string HeaderName = "X-Sync-Version";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;

        if (!request.Headers.TryGetValue(HeaderName, out var versionHeader)
            || !int.TryParse(versionHeader.FirstOrDefault(), out var version)
            || version < MinimumSyncVersion)
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return Microsoft.AspNetCore.Http.Results.Json(
                new { error = $"Sync client is outdated. Minimum version required: {MinimumSyncVersion}. Please update klavlor-sync." },
                statusCode: StatusCodes.Status426UpgradeRequired);
        }

        return await next(context);
    }
}
