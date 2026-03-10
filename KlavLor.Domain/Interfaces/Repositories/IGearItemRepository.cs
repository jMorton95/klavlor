using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface IGearItemRepository
{
    Task<GearItem?> GetById(int id);
    Task<GearItem?> GetByName(string name);
    Task<bool> SaveGearItem(GearItem gearItem);
}
