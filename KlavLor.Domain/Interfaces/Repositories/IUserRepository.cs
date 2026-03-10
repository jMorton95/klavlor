using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface IUserRepository
{
    Task<User?> GetById(int id);
    Task<int> GetCount();
    Task<User?> GetByEmail(string email);
    Task<bool> IsEmailInUse(int excludeId, string email);
    Task<bool> IsEmailInUse(string email);
    Task<bool> SaveUser(User user);
    Task<int> DeleteUser(int id);
}
