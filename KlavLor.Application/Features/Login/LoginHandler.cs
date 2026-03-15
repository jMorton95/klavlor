using Microsoft.Extensions.Logging;
using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Interfaces.Services;
using KlavLor.Domain.Services.Users;

namespace KlavLor.Application.Features.Login;

public sealed class LoginHandler(
    IUserRepository userRepository,
    UserLoginService loginService,
    IPasswordService passwordService,
    LoginValidator validator,
    TimeProvider timeProvider,
    ILogger<LoginHandler> logger)
{
    public async Task<Result<User>> Handle(LoginCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);

        if (!validationResult.IsValid)
        {
            return Result<User>.ValidationFailure(validationResult.ToDictionary());
        }

        var result = await ValidateCredentialsAsync(command);

        if (result is not { IsSuccess: false })
        {
            return Result<User>.Success(result.Value);
        }

        logger.LogWarning("Login attempt failed.");
        return Result<User>.Failure(result.ErrorMessage);
    }

    // Pre-hashed dummy value to normalize timing on failed lookups
    private static string? _dummyHash;

    private async Task<Result<User>> ValidateCredentialsAsync(LoginCommand command)
    {
        const string invalidCredentials = "Invalid email or password.";
        const string accountLocked = "Account is temporarily locked. Please try again later.";

        var user = await userRepository.GetByEmail(command.Email);

        if (user == null)
        {
            // Perform dummy hash verification to normalize timing and prevent user enumeration
            if (_dummyHash is null)
                Interlocked.CompareExchange(ref _dummyHash, passwordService.HashPassword(null!, "dummy-timing-normalization"), null);
            passwordService.CheckPassword(null!, command.Password, _dummyHash);
            return Result<User>.Failure(invalidCredentials);
        }

        if (!user.IsActive)
            return Result<User>.Failure(invalidCredentials);

        if (user.IsLockedOut && user.LockoutEnd >= timeProvider.GetUtcNow())
            return Result<User>.Failure(accountLocked);

        if (user.IsLockedOut && user.LockoutEnd < timeProvider.GetUtcNow())
        {
            loginService.HandleSuccessfulLogin(user);
        }

        if (user.HashedPassword != null && !passwordService.CheckPassword(user, command.Password, user.HashedPassword))
        {
            loginService.HandleFailedLogin(user, timeProvider.GetUtcNow());
            await userRepository.SaveUser(user);
            return Result<User>.Failure(user.IsLockedOut ? accountLocked : invalidCredentials);
        }

        loginService.HandleSuccessfulLogin(user);
        await userRepository.SaveUser(user);

        return Result<User>.Success(user);
    }
}
