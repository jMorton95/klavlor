using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface ILootRecordRepository
{
    Task<bool> SaveLootRecord(LootRecord record);
    Task<bool> SaveLootRecords(List<LootRecord> records);
    Task<HashSet<string>> FindExistingHashes(int userId, IEnumerable<string> hashes);

    Task<HashSet<string>> GetSeenItemNames(int gameCharacterId, DateTimeOffset strictlyBefore);

    Task RecomputeFirstTimeFlags(int gameCharacterId);

    Task<int> GetKillOrdinal(int gameCharacterId, string sourceName, DateTimeOffset occurredAt, int recordId);
}
