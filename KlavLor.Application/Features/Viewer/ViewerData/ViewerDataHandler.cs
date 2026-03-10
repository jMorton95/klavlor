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
        Template? template = null;

        if (query.TemplateId.HasValue)
            template = await templateRepository.GetById(query.TemplateId.Value);
        else if (!string.IsNullOrWhiteSpace(query.ShareToken))
            template = await templateRepository.GetByShareToken(query.ShareToken);

        if (template is null)
            return Result<ViewerDataResponse>.Failure("Template not found.");

        var isOwner = userId.HasValue && template.CreatedById == userId.Value;

        if (!template.IsPublic && !isOwner)
            return Result<ViewerDataResponse>.Failure("Not authorized");

        var completedNodeIds = new HashSet<int>();
        var canTrackCompletion = userId.HasValue;

        if (userId.HasValue)
        {
            var completions = await completionRepository.GetByUserAndTemplate(userId.Value, template.Id);
            completedNodeIds = completions.Select(c => c.TemplateNodeId).ToHashSet();
        }

        return Result<ViewerDataResponse>.Success(new ViewerDataResponse(
            template,
            completedNodeIds,
            isOwner,
            canTrackCompletion
        ));
    }
}
