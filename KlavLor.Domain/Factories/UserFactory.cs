using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Interfaces.Services;
using KlavLor.Domain.Shared;

namespace KlavLor.Domain.Factories;

public sealed class UserFactory(IRoleRepository roleRepository, IPasswordService passwordService)
{
    public async Task<User> CreateNewUser(string email, string firstName, string lastName, string password, bool isActive)
    {
        var defaultRole = await roleRepository.GetByName(RoleDefaults.DefaultUserRole);

        if (defaultRole is null)
        {
            throw new DomainException("Default role not found.");
        }

        var user = new User(firstName, lastName, email, isActive);

        var hashedPassword = passwordService.HashPassword(user, password);

        user.HashedPassword = hashedPassword;
        user.AssignRole(defaultRole);

        return user;
    }
}
