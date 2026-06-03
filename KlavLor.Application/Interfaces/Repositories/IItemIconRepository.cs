using KlavLor.Application.Features.Maintenance;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Repositories;

public interface IItemIconRepository
{
    Task<ItemIcon?> GetByItemName(string itemName);
    Task<List<(string Name, int ItemId)>> FindUncataloguedItems(int limit);
    Task<List<ItemIcon>> GetPendingIcons(int limit);
    Task<List<ItemIcon>> GetFailedIcons(int limit);
    Task ResetFailure(int id);
    Task<IconStats> GetStats();
    Task Save(ItemIcon icon);
    Task SaveRange(List<ItemIcon> icons);
}
