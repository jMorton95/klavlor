using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Users.GetById;

public sealed class UserGetByIdHandler(
    IUserRepository userRepository,
    UserGetByIdValidator validator)
{
    public async Task<Result<UserResponse>> Handle(UserGetByIdQuery query)
    {
        var validationResult = await validator.ValidateAsync(query);

        if (!validationResult.IsValid)
            return Result<UserResponse>.ValidationFailure(validationResult.ToDictionary());

        var user = await userRepository.GetById(query.Id!.Value);

        if (user is null)
            return Result<UserResponse>.Failure("User not found.");

        return Result<UserResponse>.Success(user.MapToResponse());
    }
}
