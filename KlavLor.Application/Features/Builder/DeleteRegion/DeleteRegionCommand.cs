namespace KlavLor.Application.Features.Builder.DeleteRegion;

public sealed class DeleteRegionCommand
{
    public int TemplateId { get; set; }
    public int RegionId { get; set; }
}
