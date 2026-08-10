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

    /// <summary>
    /// For each requested receipt, how many rolls the character did at that source since their
    /// PREVIOUS receipt of the same item — the only figure a repeat drop's luck can honestly be
    /// judged against. Absent from the result when there is no earlier receipt (a first-time drop,
    /// where the absolute kill count is already the right basis).
    /// </summary>
    /// <remarks>
    /// One batched query for the whole set, so the live publish path and the feed backfill can share
    /// it rather than each growing their own version.
    /// </remarks>
    Task<IReadOnlyDictionary<ItemReceipt, int>> GetRollsSincePreviousReceipt(IReadOnlyList<ItemReceipt> receipts);
}

public sealed record SessionKcBounds(int? MinKillCount, int? MaxKillCount, DateTimeOffset StartedAt, int FirstOrdinal);

/// One receipt of one item, identified well enough to find the one before it. OccurredAt is the
/// receiving record's own timestamp, not its feed card's.
public sealed record ItemReceipt(int GameCharacterId, string SourceName, string ItemName, DateTimeOffset OccurredAt);
