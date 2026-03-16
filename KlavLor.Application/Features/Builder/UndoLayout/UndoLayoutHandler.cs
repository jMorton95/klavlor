using System.Text.Json;
using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.UndoLayout;

public sealed class UndoLayoutHandler(
    ITemplateRepository templateRepository,
    UndoLayoutValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(UndoLayoutCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("You do not have permission to modify this template.");

        var snapshot = template.GetLatestLayoutSnapshot();
        if (snapshot is null)
            return Result.Failure("No layout history available.");

        // Restore positions from snapshot
        var positions = JsonSerializer.Deserialize<List<GroupPositionDto>>(snapshot.PositionData);
        if (positions is not null)
        {
            var groupLookup = template.Groups.ToDictionary(g => g.Id);
            foreach (var pos in positions)
            {
                if (groupLookup.TryGetValue(pos.GroupId, out var group))
                {
                    group.PositionX = pos.X;
                    group.PositionY = pos.Y;
                }
            }
        }

        template.RemoveLayoutSnapshot(snapshot.Id);
        await templateRepository.SaveTemplate(template);

        return Result.Success();
    }

    private sealed class GroupPositionDto
    {
        public int GroupId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }
}
