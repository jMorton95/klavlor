using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Loot.Ingest.Audit;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Loot.Ingest.Audit;

public sealed class SyncLogEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        // Policy "Auditor" admits Auditor OR Admin (Admin supersedes).
        return app.MapGet(AppRoutes.SyncLog.FromApi(), GetSyncLog)
            .RequireAuthorization(nameof(RoleName.Auditor))
            .AddEndpointFilter<HtmxNavigationFilter>();
    }

    private static async Task<RazorComponentResult> GetSyncLog(
        [AsParameters] IngestLogQuery query,
        IngestLogHandler handler)
    {
        var result = await handler.Handle(query);
        var data = result.Value ?? new IngestLogResult([], false, 0, 0, 0);

        // page > 1 just appends rows (+ OOB show-more button) to the existing table.
        if (query.PageNumber > 1)
            return IResultExtensions.Component<SyncLogRows>(new { Result = data, Query = query });

        return IResultExtensions.Component<SyncLogGrid>(new { Result = data, Query = query, UpdateHeader = true });
    }
}
