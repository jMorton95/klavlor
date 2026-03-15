using FluentValidation;

namespace KlavLor.Application.Features.Builder.DeleteAnnotation;

public sealed class DeleteAnnotationValidator : AbstractValidator<DeleteAnnotationCommand>
{
    public DeleteAnnotationValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.AnnotationId).GreaterThan(0);
    }
}
