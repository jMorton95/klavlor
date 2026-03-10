using Microsoft.AspNetCore.Identity;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

public class AspNetPasswordService(PasswordHasher<User> passwordHasher) : IPasswordService
{
    public string HashPassword(User user, string password)
    {
        return passwordHasher.HashPassword(user, password);
    }

    public bool CheckPassword(User user, string providedPassword, string hashedPassword)
    {
        var result = passwordHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);

        return result
            is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
