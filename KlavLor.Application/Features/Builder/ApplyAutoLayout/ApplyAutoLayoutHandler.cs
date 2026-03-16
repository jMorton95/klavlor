using System.Text.Json;
using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.ApplyAutoLayout;

public sealed class ApplyAutoLayoutHandler(
    ITemplateRepository templateRepository,
    ApplyAutoLayoutValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result> Handle(ApplyAutoLayoutCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var template = await templateRepository.GetById(command.TemplateId);
        if (template is null)
            return Result.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result.Failure("You do not have permission to modify this template.");

        // Snapshot current positions before auto-layout
        var positionData = template.Groups.Select(g => new GroupPositionDto
        {
            GroupId = g.Id,
            X = g.PositionX,
            Y = g.PositionY
        }).ToList();

        var json = JsonSerializer.Serialize(positionData);
        template.CreateLayoutSnapshot(json);

        // Apply auto-layout
        template.ApplyAutoLayout();

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
