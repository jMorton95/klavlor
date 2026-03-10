using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Services;

public interface IPasswordService
{
    string HashPassword(User user, string password);
    bool CheckPassword(User user, string providedPassword, string hashedPassword);
}
