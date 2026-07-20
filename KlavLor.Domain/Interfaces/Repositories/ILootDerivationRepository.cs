namespace KlavLor.Domain.Interfaces.Repositories;

// One loot record that needs its per-source derived metric (re)computed. DropsJson is the
// canonical drop list the strategy reasons over.
public sealed record LootDerivationRecord(int Id, string SourceName, string DropsJson);

// The computed result for one record: its estimated effective kills / roll weight.
public sealed record LootDerivationResult(int Id, int EffectiveKills);

// Backfills EffectiveKills for special-source records whose derivation is missing or stale.
// Scoped strictly to the given source names so the vast ordinary majority of the (large)
// LootRecords table is never read or written.
public interface ILootDerivationRepository
{
    // Cheap existence check so the backfill service can no-op when there's nothing to do.
    Task<bool> HasRecordsNeedingDerivation(IReadOnlyCollection<string> sources, int currentVersion);

    // Next batch of records (ordered by Id) still needing derivation at the current version.
    Task<IReadOnlyList<LootDerivationRecord>> GetBatchNeedingDerivation(
        IReadOnlyCollection<string> sources, int currentVersion, int batchSize);

    // Persist a batch of results via set-based updates that touch only EffectiveKills and the
    // version marker — no change-tracker, no audit/RowVersion churn on historical rows.
    Task ApplyDerivations(IReadOnlyCollection<LootDerivationResult> results, int version);
}
