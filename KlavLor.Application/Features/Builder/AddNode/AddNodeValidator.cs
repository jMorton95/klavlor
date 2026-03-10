using FluentValidation;

namespace KlavLor.Application.Features.Builder.AddNode;

public sealed class AddNodeValidator : AbstractValidator<AddNodeCommand>
{
    public AddNodeValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NodeType).InclusiveBetween(0, 6);
    }
}
