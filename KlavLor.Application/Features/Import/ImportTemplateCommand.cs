namespace KlavLor.Application.Features.Import;

public sealed class ImportTemplateCommand
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string JsonData { get; set; } = "";
}
