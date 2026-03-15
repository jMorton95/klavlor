using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Viewer.ToggleCompletion;

public sealed class ToggleCompletionHandler(
    ITemplateRepository templateRepository,
    IUserNodeCompletionRepository completionRepository)
{
    public async Task<Result> Handle(ToggleCompletionCommand command, int userId)
    {
        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != userId)
            return Result.Failure("Not authorized to track completion on this template.");

        var nodeExists = template.Nodes.Any(n => n.Id == command.NodeId);
        if (!nodeExists)
            return Result.Failure("Node does not belong to this template.");

        await completionRepository.Toggle(userId, command.NodeId, command.Note);

        return Result.Success();
    }
}
