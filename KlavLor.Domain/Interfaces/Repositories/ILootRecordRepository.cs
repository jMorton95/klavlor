using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface ILootRecordRepository
{
    Task<bool> SaveLootRecord(LootRecord record);
    Task<bool> SaveLootRecords(List<LootRecord> records);
    Task<HashSet<string>> FindExistingHashes(int userId, IEnumerable<string> hashes);
}
