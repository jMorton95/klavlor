namespace KlavLor.Application.Features.Builder.UpdateRegion;

public sealed class UpdateRegionCommand
{
    public int TemplateId { get; set; }
    public int RegionId { get; set; }
    public string? Label { get; set; }
    public string Color { get; set; } = "slate";
    public double Opacity { get; set; } = 0.15;
}
