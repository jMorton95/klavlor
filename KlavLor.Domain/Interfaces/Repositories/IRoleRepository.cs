using KlavLor.Domain.Entities;
using KlavLor.Domain.Shared;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByName(RoleName roleName);
}
