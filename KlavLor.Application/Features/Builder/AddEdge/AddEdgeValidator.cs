using FluentValidation;

namespace KlavLor.Application.Features.Builder.AddEdge;

public sealed class AddEdgeValidator : AbstractValidator<AddEdgeCommand>
{
    public AddEdgeValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);

        // Either FromNodeId or FromGroupId must be provided
        RuleFor(x => x)
            .Must(x => x.FromNodeId > 0 || x.FromGroupId is > 0)
            .WithMessage("A source node or group is required.");

        // Either ToNodeId or ToGroupId must be provided
        RuleFor(x => x)
            .Must(x => x.ToNodeId > 0 || x.ToGroupId is > 0)
            .WithMessage("A target node or group is required.");

        // Can't connect to self (node-to-node case)
        RuleFor(x => x)
            .Must(x => x.FromNodeId != x.ToNodeId || x.FromNodeId == 0)
            .WithMessage("Cannot create an edge from a node to itself.");
    }
}
