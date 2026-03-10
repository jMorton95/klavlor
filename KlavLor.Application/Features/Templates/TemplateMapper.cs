using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Templates;

public static class TemplateMapper
{
    public static TemplateResponse MapToResponse(this Template template)
    {
        return new TemplateResponse(
            template.Id,
            template.Name,
            template.Description,
            template.IsPublic,
            template.ShareToken,
            template.CreatedBy?.FirstName + " " + template.CreatedBy?.LastName ?? "Unknown",
            template.Nodes.Count,
            template.Edges.Count,
            template.SavedAt
        );
    }
}
