namespace KlavLor.Application.Features.Templates.Create;

public sealed class TemplateCreateCommand
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public int? SourceTemplateId { get; set; }
}
