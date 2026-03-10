using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.Export;

public sealed class ExportTemplateHandler(ITemplateRepository templateRepository)
{
    public async Task<Result<ExportTemplateResponse>> Handle(int templateId, int userId)
    {
        var template = await templateRepository.GetById(templateId);
        if (template is null)
            return Result<ExportTemplateResponse>.Failure("Template not found.");

        if (template.CreatedById != userId && !template.IsPublic)
            return Result<ExportTemplateResponse>.Failure("You do not have access to this template.");

        var response = new ExportTemplateResponse(
            template.Name,
            template.Description,
            template.Nodes.Select(n => new ExportNode(
                n.Id, n.Label, n.NodeType.ToString(),
                n.PositionX, n.PositionY, n.Metadata, n.IconUrl
            )).ToArray(),
            template.Edges.Select(e => new ExportEdge(
                e.FromNodeId, e.ToNodeId
            )).ToArray()
        );

        return Result<ExportTemplateResponse>.Success(response);
    }
}
