namespace KlavLor.Application.Features.Builder.AddRegion;

public sealed class AddRegionCommand
{
    public int TemplateId { get; set; }
    public string? Label { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;
    public string Color { get; set; } = "slate";
    public double Opacity { get; set; } = 0.15;
}
