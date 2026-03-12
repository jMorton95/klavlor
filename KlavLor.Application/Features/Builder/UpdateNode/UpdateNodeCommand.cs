namespace KlavLor.Application.Features.Builder.UpdateNode;

public sealed class UpdateNodeCommand
{
    public int TemplateId { get; set; }
    public int NodeId { get; set; }
    public string Label { get; set; } = "";
    public int NodeType { get; set; }
    public string? IconUrl { get; set; }
    public string? Color { get; set; }
}
