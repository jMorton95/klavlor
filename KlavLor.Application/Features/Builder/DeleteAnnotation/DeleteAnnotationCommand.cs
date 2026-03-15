namespace KlavLor.Application.Features.Builder.DeleteAnnotation;

public sealed class DeleteAnnotationCommand
{
    public int TemplateId { get; set; }
    public int AnnotationId { get; set; }
}
