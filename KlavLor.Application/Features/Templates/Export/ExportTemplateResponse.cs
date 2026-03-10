namespace KlavLor.Application.Features.Templates.Export;

public sealed record ExportTemplateResponse(
    string Name,
    string? Description,
    ExportNode[] Nodes,
    ExportEdge[] Edges
);

public sealed record ExportNode(
    int Id,
    string Label,
    string NodeType,
    double PositionX,
    double PositionY,
    string? Metadata,
    string? IconUrl
);

public sealed record ExportEdge(
    int FromNodeId,
    int ToNodeId
);
