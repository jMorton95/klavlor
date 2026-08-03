using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Builder.AddAnnotation;
using KlavLor.Application.Features.Builder.UpdateAnnotation;
using KlavLor.Application.Features.Builder.UpdateAnnotationPosition;
using KlavLor.Application.Features.Builder.DeleteAnnotation;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class AnnotationEndpoints : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.BuilderAnnotationCreate.FromApi(), GetCreateModal).RequireAuthorization(nameof(RoleName.User));
        app.MapPost(AppRoutes.BuilderAnnotations.FromApi(), AddAnnotation).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        app.MapGet(AppRoutes.BuilderAnnotationEdit.FromApi(), GetEditModal).RequireAuthorization(nameof(RoleName.User));
        app.MapPut(AppRoutes.BuilderAnnotation.FromApi(), UpdateAnnotation).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
        app.MapPut(AppRoutes.BuilderAnnotationPosition.FromApi(), UpdateAnnotationPosition).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("position");
        return app.MapDelete(AppRoutes.BuilderAnnotation.FromApi(), DeleteAnnotation).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
    }

    private static IResult GetCreateModal(
        int id,
        [FromQuery] double posX,
        [FromQuery] double posY,
        ISessionStateManager sessionManager)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        return IResultExtensions.Component<AnnotationCreateModal>(new
        {
            TemplateId = id,
            PositionX = posX > 0 ? posX : 400,
            PositionY = posY > 0 ? posY : 300
        });
    }

    private static async Task<IResult> AddAnnotation(
        [FromForm] AddAnnotationCommand command,
        ISessionStateManager sessionManager,
        AddAnnotationHandler handler,
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
        int id, int annotationId,
        ISessionStateManager sessionManager,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var template = await templateRepository.GetById(id);
        if (template is null || (template.CreatedById != userId.Value && !sessionManager.IsUserSessionAdministrator()))
            return Results.NotFound();

        var annotation = template.Annotations.FirstOrDefault(a => a.Id == annotationId);
        if (annotation is null) return Results.NotFound();

        return IResultExtensions.Component<AnnotationEditModal>(new
        {
            TemplateId = id,
            AnnotationId = annotationId,
            Text = annotation.Text,
            CurrentFontSize = annotation.FontSize
        });
    }

    private static async Task<IResult> UpdateAnnotation(
        [FromForm] UpdateAnnotationCommand command,
        ISessionStateManager sessionManager,
        UpdateAnnotationHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var result = await handler.Handle(command);
        if (!result.IsSuccess) return Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null) return Results.NotFound();

        var annotation = template.Annotations.FirstOrDefault(a => a.Id == command.AnnotationId);
        if (annotation is null) return Results.NotFound();

        return IResultExtensions.Component<BuilderAnnotation>(new
        {
            Annotation = annotation,
            TemplateId = command.TemplateId
        });
    }

    private static async Task<IResult> UpdateAnnotationPosition(
        int id, int annotationId,
        [FromBody] UpdateAnnotationPositionCommand command,
        ISessionStateManager sessionManager,
        UpdateAnnotationPositionHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        command.TemplateId = id;
        command.AnnotationId = annotationId;
        var result = await handler.Handle(command);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.BadRequest(result.ErrorMessage);
    }

    private static async Task<IResult> DeleteAnnotation(
        int id, int annotationId,
        ISessionStateManager sessionManager,
        DeleteAnnotationHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Results.Unauthorized();

        var command = new DeleteAnnotationCommand { TemplateId = id, AnnotationId = annotationId };
        var result = await handler.Handle(command);

        return result.IsSuccess
            ? Results.NoContent()
            : Results.BadRequest(result.ErrorMessage);
    }
}
