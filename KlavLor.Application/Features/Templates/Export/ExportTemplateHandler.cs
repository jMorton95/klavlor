using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.Export;

public sealed class ExportTemplateHandler(ITemplateRepository templateRepository, ICurrentUser currentUser)
{
    public async Task<Result<ExportTemplateResponse>> Handle(int templateId)
    {
        var template = await templateRepository.GetById(templateId);
        if (template is null)
            return Result<ExportTemplateResponse>.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !template.IsPublic && !currentUser.IsAdmin)
            return Result<ExportTemplateResponse>.Failure("You do not have access to this template.");

        var response = new ExportTemplateResponse(
            template.Name,
            template.Description,
            template.Nodes.Select(n => new ExportNode(
                n.Id, n.Label, n.NodeType.ToString(),
                n.PositionX, n.PositionY, n.Metadata, n.IconUrl,
                n.SortOrder, n.Color
            )).ToArray(),
            template.Edges.Select(e => new ExportEdge(
                e.FromNodeId, e.ToNodeId
            )).ToArray()
        );

        return Result<ExportTemplateResponse>.Success(response);
    }
}
