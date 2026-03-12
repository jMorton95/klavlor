using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Users;

internal sealed class UserRepository(DataContext dataContext, ILogger<UserRepository> logger) : IUserRepository
{
    public async Task<User?> GetById(int id)
    {
        try
        {
            return await dataContext.Users
                .Include(x => x.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(x => x.Id == id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get user by id {UserId}", id);
            throw new RepositoryException("Failed to get user by id", ex);
        }
    }

    public async Task<int> GetCount()
    {
        try
        {
            return await dataContext.Users.CountAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get User Count.");
            throw new RepositoryException("Failed to get user count", ex);
        }
    }

    public async Task<User?> GetByEmail(string email)
    {
        try
        {
            return await dataContext.Users.FirstOrDefaultAsync(x => x.Email == email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get user by email {Email}", email);
            throw new RepositoryException("Failed to get user by email", ex);
        }
    }

    public async Task<bool> IsEmailInUse(string email)
    {
        try
        {
            return await dataContext.Users.AnyAsync(u => u.Email == email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check if Email was in use: {email}", email);
            throw new RepositoryException("Failed to check email address.", ex);
        }
    }

    public async Task<bool> IsEmailInUse(int excludeId, string email)
    {
        try
        {
            return await dataContext.Users.AnyAsync(u => u.Email == email && u.Id != excludeId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check if Email was in use: {email}, excluding: {excludeId}", email, excludeId);
            throw new RepositoryException("Failed to check email address.", ex);
        }
    }

    public async Task<bool> SaveUser(User user)
    {
        try
        {
            if (user.Id == 0)
                dataContext.Users.Add(user);
            else
                dataContext.Users.Update(user);

            return await dataContext.SaveChangesAsync() > 0;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save user {UserId}", user.Id);
            throw new RepositoryException("Failed to save user", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error saving user {UserId}", user.Id);
            throw new RepositoryException("Unexpected error saving user", ex);
        }
    }

    public async Task<int> DeleteUser(int id)
    {
        try
        {
            return await dataContext.Users.Where(x => x.Id == id).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete user {UserId}", id);
            throw new RepositoryException("Failed to delete user", ex);
        }
    }
}
