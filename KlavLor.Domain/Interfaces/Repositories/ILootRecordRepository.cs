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
    /// The same figure as <see cref="GetKillOrdinal"/> for a whole batch, in ONE round-trip,
    /// keyed by record id.
    /// </summary>
    /// <remarks>
    /// Exists because the single-record overload costs two round-trips (the count and the admin
    /// baseline), and the live roll ticker needs an ordinal for EVERY kill rather than only the ones
    /// valuable enough to reach the feed. At 250 records to a sync batch that is 500 queries; this
    /// is one, doing the same indexed work per row inside the server.
    ///
    /// Both callers go through this so the roll ticker and the feed card can never quote different
    /// roll numbers for the same kill - the drift CLAUDE.md keeps warning about.
    /// </remarks>
    Task<Dictionary<int, int>> GetKillOrdinals(IReadOnlyCollection<KillOrdinalRequest> requests);

    /// <summary>
    /// Bounds of the play session (per the site session rules, parameterised by
    /// <paramref name="gap"/>/<paramref name="breakGap"/>) that contains the kill at
    /// <paramref name="occurredAt"/>: the session's first kill time, its min/max reported
    /// KillCount across every kill in the session, and the chronological ordinal of the
    /// session's first kill. Null when the character has no kills at the source.
    /// </summary>
    Task<SessionKcBounds?> GetSessionBounds(int gameCharacterId, string sourceName, DateTimeOffset occurredAt, TimeSpan gap, TimeSpan breakGap);
}

/// <summary>One record to resolve a kill ordinal for. RecordId is the key the result comes back on.</summary>
public sealed record KillOrdinalRequest(int RecordId, int GameCharacterId, string SourceName, DateTimeOffset OccurredAt);

public sealed record SessionKcBounds(int? MinKillCount, int? MaxKillCount, DateTimeOffset StartedAt, int FirstOrdinal);
