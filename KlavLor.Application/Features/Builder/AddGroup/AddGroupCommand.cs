namespace KlavLor.Application.Features.Builder.AddGroup;

public sealed class AddGroupCommand
{
    public int TemplateId { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}
