using FluentValidation;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Users.Create;

public sealed class UserCreateValidator : AbstractValidator<UserCreateCommand>
{
    public UserCreateValidator(IUserRepository userRepository)
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress()
            .MustAsync(async (email, ct) => !await userRepository.IsEmailInUse(email))
            .WithMessage("Email address is already in use.");
        RuleFor(x => x.PasswordOne).NotEmpty().MinimumLength(12)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one number.");
        RuleFor(x => x.PasswordTwo).Equal(x => x.PasswordOne).WithMessage("Passwords must match.");
    }
}
