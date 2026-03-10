namespace KlavLor.Application.Features.Builder.AssignNodeToGroup;

public sealed class AssignNodeToGroupCommand
{
    public int TemplateId { get; set; }
    public int NodeId { get; set; }
    public int? GroupId { get; set; }
}
