using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Builder.AddRegion;
using KlavLor.Application.Features.Builder.UpdateRegion;
using KlavLor.Application.Features.Builder.UpdateRegionPosition;
using KlavLor.Application.Features.Builder.UpdateRegionSize;
using KlavLor.Application.Features.Builder.DeleteRegion;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class RegionEndpoints : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost(AppRoutes.BuilderRegions.FromApi(), AddRegion).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        app.MapGet(AppRoutes.BuilderRegionEdit.FromApi(), GetEditModal).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("read");
        app.MapPut(AppRoutes.BuilderRegion.FromApi(), UpdateRegion).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        app.MapPut(AppRoutes.BuilderRegionPosition.FromApi(), UpdateRegionPosition).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("position");
        app.MapPut(AppRoutes.BuilderRegionSize.FromApi(), UpdateRegionSize).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("position");
        return app.MapDelete(AppRoutes.BuilderRegion.FromApi(), DeleteRegion).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
    }

    private static async Task<IResult> AddRegion(
        [FromForm] AddRegionCommand command,
        ISessionStateManager sessionManager,
        AddRegionHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        return IResultExtensions.Component<BuilderCanvas>(new { Template = template });
    }

    private static async Task<IResult> GetEditModal(
        int id, int regionId,
        ISessionStateManager sessionManager,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var template = await templateRepository.GetById(id);
        if (template is null || (template.CreatedById != userId.Value && !sessionManager.IsUserSessionAdministrator()))
            return Results.NotFound();

        var region = template.Regions.FirstOrDefault(r => r.Id == regionId);
        if (region is null) return Results.NotFound();

        return IResultExtensions.Component<RegionEditModal>(new
        {
            TemplateId = id,
            RegionId = regionId,
            Label = region.Label,
            CurrentColor = region.Color,
            CurrentOpacity = region.Opacity
        });
    }

    private static async Task<IResult> UpdateRegion(
        [FromForm] UpdateRegionCommand command,
        ISessionStateManager sessionManager,
        UpdateRegionHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null) return Results.NotFound();

        var region = template.Regions.FirstOrDefault(r => r.Id == command.RegionId);
        if (region is null) return Results.NotFound();

        return IResultExtensions.Component<BuilderRegion>(new
        {
            Region = region,
            TemplateId = command.TemplateId
        });
    }

    private static async Task<IResult> UpdateRegionPosition(
        int id, int regionId,
        [FromBody] UpdateRegionPositionCommand command,
        ISessionStateManager sessionManager,
        UpdateRegionPositionHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        command.TemplateId = id;
        command.RegionId = regionId;
        var result = await handler.Handle(command);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.BadRequest(result.ErrorMessage);
    }

    private static async Task<IResult> UpdateRegionSize(
        int id, int regionId,
        [FromBody] UpdateRegionSizeCommand command,
        ISessionStateManager sessionManager,
        UpdateRegionSizeHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        command.TemplateId = id;
        command.RegionId = regionId;
        var result = await handler.Handle(command);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.BadRequest(result.ErrorMessage);
    }

    private static async Task<IResult> DeleteRegion(
        int id, int regionId,
        ISessionStateManager sessionManager,
        DeleteRegionHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var command = new DeleteRegionCommand { TemplateId = id, RegionId = regionId };
        var result = await handler.Handle(command);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.BadRequest(result.ErrorMessage);
    }
}
