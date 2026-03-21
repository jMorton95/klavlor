using KlavLor.Application.Features.Loot.Ingest;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace KlavLor.Web.Application.Features.Loot.Ingest;

public sealed class LootIngestEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost(AppRoutes.LootIngest.FromApi(), Ingest)
            .RequireAuthorization()
            .RequireRateLimiting("loot-ingest")
            .AddEndpointFilter<SyncVersionFilter>()
            .DisableAntiforgery();

        return app.MapPost(AppRoutes.LootIngestBatch.FromApi(), IngestBatch)
            .RequireAuthorization()
            .RequireRateLimiting("loot-ingest")
            .AddEndpointFilter<SyncVersionFilter>()
            .DisableAntiforgery();
    }

    private static async Task<IResult> Ingest(
        [FromBody] LootIngestCommand command,
        LootIngestHandler handler)
    {
        var result = await handler.Handle(command);
        return result.IsSuccess
            ? TypedResults.Created()
            : TypedResults.BadRequest(new { error = result.ErrorMessage });
    }

    private static async Task<IResult> IngestBatch(
        [FromBody] List<LootIngestCommand> commands,
        LootIngestHandler handler)
    {
        var result = await handler.HandleBatch(commands);
        return result.IsSuccess
            ? TypedResults.Created()
            : TypedResults.BadRequest(new { error = result.ErrorMessage });
    }
}
