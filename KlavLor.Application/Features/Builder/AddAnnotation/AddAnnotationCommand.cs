namespace KlavLor.Application.Features.Builder.AddAnnotation;

public sealed class AddAnnotationCommand
{
    public int TemplateId { get; set; }
    public string Text { get; set; } = "";
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public string FontSize { get; set; } = "medium";
}
