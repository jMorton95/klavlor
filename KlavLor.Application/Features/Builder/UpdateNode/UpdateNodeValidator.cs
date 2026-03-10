using FluentValidation;

namespace KlavLor.Application.Features.Builder.UpdateNode;

public sealed class UpdateNodeValidator : AbstractValidator<UpdateNodeCommand>
{
    public UpdateNodeValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.NodeId).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NodeType).InclusiveBetween(0, 6);
    }
}
