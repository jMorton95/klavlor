using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Viewer.ToggleCompletion;

public sealed class ToggleCompletionHandler(
    ITemplateRepository templateRepository,
    IUserNodeCompletionRepository completionRepository,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(ToggleCompletionCommand command)
    {
        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("Not authorized to track completion on this template.");

        var nodeExists = template.Nodes.Any(n => n.Id == command.NodeId);
        if (!nodeExists)
            return Result.Failure("Node does not belong to this template.");

        await completionRepository.Toggle(currentUser.UserId!.Value, command.NodeId, command.Note);

        return Result.Success();
    }
}
