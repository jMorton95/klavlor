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
        RuleFor(x => x.IconUrl)
            .Must(url => string.IsNullOrEmpty(url)
                || url.StartsWith("data:image/")
                || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"))
            .WithMessage("Icon URL must be a valid HTTPS URL or an embedded image.");
        RuleFor(x => x.Color)
            .Must(c => string.IsNullOrEmpty(c) || ValidColors.Contains(c))
            .WithMessage("Invalid colour.");
    }

    private static readonly HashSet<string> ValidColors = ["amber", "blue", "purple", "green", "orange", "red", "indigo"];
}
