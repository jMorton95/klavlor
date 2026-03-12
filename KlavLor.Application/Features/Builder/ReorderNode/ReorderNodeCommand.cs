namespace KlavLor.Application.Features.Builder.ReorderNode;

public sealed class ReorderNodeCommand
{
    public int TemplateId { get; set; }
    public int NodeId { get; set; }
    public string Direction { get; set; } = "";
}
