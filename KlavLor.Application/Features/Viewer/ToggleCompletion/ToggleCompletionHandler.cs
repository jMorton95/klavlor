using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Viewer.ToggleCompletion;

public sealed class ToggleCompletionHandler(
    ITemplateRepository templateRepository,
    IUserNodeCompletionRepository completionRepository)
{
    public async Task<Result> Handle(ToggleCompletionCommand command, int userId)
    {
        var nodeExists = await templateRepository.NodeBelongsToTemplate(command.NodeId, command.TemplateId);
        if (!nodeExists)
            return Result.Failure("Node does not belong to this template.");

        await completionRepository.Toggle(userId, command.NodeId);

        return Result.Success();
    }
}
