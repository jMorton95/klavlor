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

        // COMPLETION BELONGS TO THE TEMPLATE, NOT TO WHOEVER CLICKED, so the tick is always written
        // against the owner. That is the model ViewerDataHandler already reads - it loads the
        // owner's rows so every viewer sees one shared progress state - and this write used to
        // disagree with it, targeting currentUser instead.
        //
        // The disagreement was invisible to the owner (for them the two ids are the same) and
        // silent for an admin: the toggle succeeded, wrote a row under the ADMIN's account, and the
        // viewer then rendered the owner's rows, which had not changed. The tick survived the htmx
        // swap and vanished on the next load, so it read as "admin cannot check nodes" while in
        // fact it was checking a node nothing displays. Notes went the same way.
        //
        // Only the owner and admins reach this line (the check above), so the owner is the right
        // target for both: an admin is filling the sheet in on the owner's behalf.
        await completionRepository.Toggle(template.CreatedById, command.NodeId, command.Note);

        return Result.Success();
    }
}
