using FluentValidation;

namespace KlavLor.Application.Features.Login;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).WithMessage("Email is required.");
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128).WithMessage("Password is required.");
    }
}
