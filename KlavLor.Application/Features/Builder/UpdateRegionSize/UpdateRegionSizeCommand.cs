namespace KlavLor.Application.Features.Builder.UpdateRegionSize;

public sealed class UpdateRegionSizeCommand
{
    public int TemplateId { get; set; }
    public int RegionId { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
