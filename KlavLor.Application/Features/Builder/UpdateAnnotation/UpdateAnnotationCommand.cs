namespace KlavLor.Application.Features.Builder.UpdateAnnotation;

public sealed class UpdateAnnotationCommand
{
    public int TemplateId { get; set; }
    public int AnnotationId { get; set; }
    public string Text { get; set; } = "";
    public string FontSize { get; set; } = "medium";
}
