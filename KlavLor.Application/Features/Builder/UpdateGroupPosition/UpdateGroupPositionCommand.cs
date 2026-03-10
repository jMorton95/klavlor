namespace KlavLor.Application.Features.Builder.UpdateGroupPosition;

public sealed class UpdateGroupPositionCommand
{
    public int TemplateId { get; set; }
    public int GroupId { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}
