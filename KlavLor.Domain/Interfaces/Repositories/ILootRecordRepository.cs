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

    /// <summary>
    /// Bounds of the play session (per the site session rules, parameterised by
    /// <paramref name="gap"/>/<paramref name="breakGap"/>) that contains the kill at
    /// <paramref name="occurredAt"/>: the session's first kill time, its min/max reported
    /// KillCount across every kill in the session, and the chronological ordinal of the
    /// session's first kill. Null when the character has no kills at the source.
    /// </summary>
    Task<SessionKcBounds?> GetSessionBounds(int gameCharacterId, string sourceName, DateTimeOffset occurredAt, TimeSpan gap, TimeSpan breakGap);
}

public sealed record SessionKcBounds(int? MinKillCount, int? MaxKillCount, DateTimeOffset StartedAt, int FirstOrdinal);
