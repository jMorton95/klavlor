using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Factories;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Users.Create;

public sealed class UserCreateHandler(
    UserFactory userFactory,
    IUserRepository userRepository,
    UserCreateValidator validator)
{
    public async Task<Result<User>> Handle(UserCreateCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result<User>.ValidationFailure(validationResult.ToDictionary());

        var user = await userFactory.CreateNewUser(
            command.Email, command.FirstName, command.LastName, command.PasswordOne, true);

        await userRepository.SaveUser(user);

        return Result<User>.Success(user);
    }
}
