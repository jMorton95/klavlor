using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Viewer.ViewerData;

public sealed class ViewerDataHandler(
    ITemplateRepository templateRepository,
    IUserNodeCompletionRepository completionRepository,
    ICurrentUser currentUser)
{
    public async Task<Result<ViewerDataResponse>> Handle(ViewerDataQuery query)
    {
        if (!query.TemplateId.HasValue)
            return Result<ViewerDataResponse>.Failure("Template not found.");

        var template = await templateRepository.GetById(query.TemplateId.Value);

        if (template is null)
            return Result<ViewerDataResponse>.Failure("Template not found.");

        var isOwner = currentUser.UserId.HasValue && template.CreatedById == currentUser.UserId.Value;

        if (!template.IsPublic && !isOwner && !currentUser.IsAdmin)
            return Result<ViewerDataResponse>.Failure("Not authorized");

        var canTrackCompletion = isOwner || currentUser.IsAdmin;

        // Always load the owner's completions so every viewer sees the template's
        // progress state. Only the owner can toggle (enforced by ToggleCompletionHandler).
        var completions = await completionRepository.GetByUserAndTemplate(template.CreatedById, template.Id);
        var completionDates = completions.ToDictionary(
            c => c.TemplateNodeId,
            c => new CompletionInfo(c.CompletedAt, c.Note));

        return Result<ViewerDataResponse>.Success(new ViewerDataResponse(
            template,
            completionDates,
            isOwner,
            canTrackCompletion,
            currentUser.UserId.HasValue
        ));
    }
}
