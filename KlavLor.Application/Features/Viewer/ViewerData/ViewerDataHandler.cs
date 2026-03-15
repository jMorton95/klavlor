using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Viewer.ViewerData;

public sealed class ViewerDataHandler(
    ITemplateRepository templateRepository,
    IUserNodeCompletionRepository completionRepository)
{
    public async Task<Result<ViewerDataResponse>> Handle(ViewerDataQuery query, int? userId)
    {
        if (!query.TemplateId.HasValue)
            return Result<ViewerDataResponse>.Failure("Template not found.");

        var template = await templateRepository.GetById(query.TemplateId.Value);

        if (template is null)
            return Result<ViewerDataResponse>.Failure("Template not found.");

        var isOwner = userId.HasValue && template.CreatedById == userId.Value;

        if (!template.IsPublic && !isOwner)
            return Result<ViewerDataResponse>.Failure("Not authorized");

        var canTrackCompletion = isOwner;

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
            canTrackCompletion
        ));
    }
}
