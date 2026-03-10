using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Users.Edit;

public sealed class UserEditHandler(
    IUserRepository userRepository,
    UserEditValidator validator)
{
    public async Task<Result<User>> Handle(UserEditCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result<User>.ValidationFailure(validationResult.ToDictionary());

        var user = await userRepository.GetById(command.Id);

        if (user is null)
            return Result<User>.Failure("User not found.");

        user.UpdateProfile(command.FirstName, command.LastName, command.Email, command.IsActive);
        await userRepository.SaveUser(user);

        return Result<User>.Success(user);
    }
}
