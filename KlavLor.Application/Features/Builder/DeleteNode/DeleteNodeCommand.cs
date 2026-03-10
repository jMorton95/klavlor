namespace KlavLor.Application.Features.Builder.DeleteNode;

public sealed class DeleteNodeCommand
{
    public int TemplateId { get; set; }
    public int NodeId { get; set; }
}
