using FluentValidation;

namespace KlavLor.Application.Features.Builder.AddNode;

public sealed class AddNodeValidator : AbstractValidator<AddNodeCommand>
{
    public AddNodeValidator()
    {
        RuleFor(x => x.TemplateId).GreaterThan(0);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NodeType).InclusiveBetween(0, 6);
        RuleFor(x => x.PositionX).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
        RuleFor(x => x.PositionY).InclusiveBetween(-10000, 100000)
            .Must(v => !double.IsNaN(v) && !double.IsInfinity(v));
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
