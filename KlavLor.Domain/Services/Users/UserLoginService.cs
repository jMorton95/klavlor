using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Services.Users;

public sealed class UserLoginService
{
    private const int MaxFailedAttempts = 3;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromHours(1);

    public void HandleFailedLogin(User user, DateTimeOffset now)
    {
        if (user.IsLockedOut && user.LockoutEnd < now)
        {
            ResetFailedAttempts(user);
        }

        if (!user.IsLockedOut)
        {
            user.AccessFailedCount += 1;

            if (user.AccessFailedCount >= MaxFailedAttempts)
            {
                user.IsLockedOut = true;
                user.LockoutEnd = now + LockoutDuration;
            }
        }
    }

    public void HandleSuccessfulLogin(User user)
    {
        ResetFailedAttempts(user);
    }

    private void ResetFailedAttempts(User user)
    {
        user.IsLockedOut = false;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
    }
}
