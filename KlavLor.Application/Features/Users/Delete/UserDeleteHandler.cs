using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Users.Delete;

public sealed class UserDeleteHandler(
    IUserRepository userRepository,
    UserDeleteValidator validator)
{
    public async Task<Result> Handle(UserDeleteCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
            return Result.ValidationFailure(validationResult.ToDictionary());

        var result = await userRepository.DeleteUser(command.Id!.Value);

        return result > 0
            ? Result.Success()
            : Result.Failure("Failed to delete user.");
    }
}
