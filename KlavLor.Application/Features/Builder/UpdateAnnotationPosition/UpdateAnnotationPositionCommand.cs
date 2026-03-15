namespace KlavLor.Application.Features.Builder.UpdateAnnotationPosition;

public sealed class UpdateAnnotationPositionCommand
{
    public int TemplateId { get; set; }
    public int AnnotationId { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}
