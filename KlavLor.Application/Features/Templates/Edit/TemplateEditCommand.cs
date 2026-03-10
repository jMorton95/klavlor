namespace KlavLor.Application.Features.Templates.Edit;

public sealed class TemplateEditCommand
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
}
