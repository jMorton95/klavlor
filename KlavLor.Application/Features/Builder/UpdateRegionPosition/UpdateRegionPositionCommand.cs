namespace KlavLor.Application.Features.Builder.UpdateRegionPosition;

public sealed class UpdateRegionPositionCommand
{
    public int TemplateId { get; set; }
    public int RegionId { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}
