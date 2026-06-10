using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Users.Account;

public sealed class SidebarAccountHandler(
    IUserRepository userRepository,
    ICurrentUser currentUser)
{
    public async Task<Result<SidebarAccountResponse>> Handle()
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result<SidebarAccountResponse>.Failure("User not authenticated.");

        var user = await userRepository.GetById(userId.Value);
        if (user is null)
            return Result<SidebarAccountResponse>.Failure("User not found.");

        return Result<SidebarAccountResponse>.Success(
            new SidebarAccountResponse($"{user.FirstName} {user.LastName}", user.Email));
    }
}
