namespace KlavLor.Application.Features.Builder.AddEdge;

public sealed class AddEdgeCommand
{
    public int TemplateId { get; set; }
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public int? FromGroupId { get; set; }
    public int? ToGroupId { get; set; }
}
