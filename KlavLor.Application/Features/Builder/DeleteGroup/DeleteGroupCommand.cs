namespace KlavLor.Application.Features.Builder.DeleteGroup;

public sealed class DeleteGroupCommand
{
    public int TemplateId { get; set; }
    public int GroupId { get; set; }
}
