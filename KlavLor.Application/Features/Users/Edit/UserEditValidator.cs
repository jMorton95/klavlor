using FluentValidation;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Users.Edit;

public sealed class UserEditValidator : AbstractValidator<UserEditCommand>
{
    public UserEditValidator(IUserRepository userRepository)
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress()
            .MustAsync(async (cmd, email, ct) => !await userRepository.IsEmailInUse(cmd.Id, email))
            .WithMessage("Email address is already in use.");
    }
}
