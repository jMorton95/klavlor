namespace KlavLor.Application.Features.Viewer.ToggleCompletion;

public sealed class ToggleCompletionCommand
{
    public int TemplateId { get; set; }
    public int NodeId { get; set; }
    public string? Note { get; set; }
}
