namespace KlavLor.Application.Features.Builder.AddNode;

public sealed class AddNodeCommand
{
    public int TemplateId { get; set; }
    public string Label { get; set; } = "";
    public int NodeType { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public string? IconUrl { get; set; }
    public int? GroupId { get; set; }
}
