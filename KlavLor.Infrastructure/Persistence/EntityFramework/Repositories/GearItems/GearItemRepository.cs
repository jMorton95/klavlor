using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.GearItems;

internal sealed class GearItemRepository(DataContext dataContext, ILogger<GearItemRepository> logger) : IGearItemRepository
{
    public async Task<GearItem?> GetById(int id)
    {
        try
        {
            return await dataContext.GearItems.FirstOrDefaultAsync(g => g.Id == id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get gear item by id {GearItemId}", id);
            throw new RepositoryException("Failed to get gear item", ex);
        }
    }

    public async Task<GearItem?> GetByName(string name)
    {
        try
        {
            return await dataContext.GearItems.FirstOrDefaultAsync(g => g.Name == name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get gear item by name {Name}", name);
            throw new RepositoryException("Failed to get gear item by name", ex);
        }
    }

    public async Task<bool> SaveGearItem(GearItem gearItem)
    {
        try
        {
            if (gearItem.Id == 0)
                dataContext.GearItems.Add(gearItem);
            else
                dataContext.GearItems.Update(gearItem);

            return await dataContext.SaveChangesAsync() > 0;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save gear item");
            throw new RepositoryException("Failed to save gear item", ex);
        }
    }
}
