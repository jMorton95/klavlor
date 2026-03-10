using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Roles;

internal sealed class RoleRepository(DataContext dataContext, ILogger<RoleRepository> logger) : IRoleRepository
{
    public async Task<Role?> GetByName(RoleName roleName)
    {
        try
        {
            return await dataContext.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get role by name {RoleName}", roleName);
            throw new RepositoryException("Failed to get role", ex);
        }
    }
}
