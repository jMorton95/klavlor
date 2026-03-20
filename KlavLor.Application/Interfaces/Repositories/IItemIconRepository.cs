using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Repositories;

public interface IItemIconRepository
{
    Task<ItemIcon?> GetByItemName(string itemName);
    Task<List<(string Name, int ItemId)>> FindUncataloguedItems(int limit);
    Task<List<ItemIcon>> GetPendingIcons(int limit);
    Task Save(ItemIcon icon);
    Task SaveRange(List<ItemIcon> icons);
}
