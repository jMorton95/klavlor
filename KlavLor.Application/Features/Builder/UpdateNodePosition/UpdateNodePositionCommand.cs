namespace KlavLor.Application.Features.Builder.UpdateNodePosition;

public sealed class UpdateNodePositionCommand
{
    public int TemplateId { get; set; }
    public int NodeId { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}
