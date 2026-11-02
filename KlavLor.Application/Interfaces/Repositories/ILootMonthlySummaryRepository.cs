using KlavLor.Application.Features.Loot.Summary;

namespace KlavLor.Application.Interfaces.Repositories;

// The monthly summary's own queries. Every one is bounded by a single month of a single character
// (or one user's handful of characters) on IX_LootRecords_GameCharacterId_OccurredAt and its
// siblings, so cost grows with that month's activity rather than with the table. The day-by-day
// calendar is NOT here: it is ILootProfileRepository.GetActivityCalendar, shared with the heatmap so
// the two cannot disagree about what counted as a new collection-log slot on a given day.
public interface ILootMonthlySummaryRepository
{
    /// <summary>
    /// Every character the user owns, with its kills, active hour buckets, active days and GP in
    /// [from, to), ordered MOST ACTIVE FIRST: by distinct active hours, then kills, then GP.
    ///
    /// Hours lead, not kills, because kill rate varies by two orders of magnitude between content —
    /// an alt chinning a few thousand monsters in an evening would otherwise outrank a main that
    /// raided every night of the month. Time spent is what "the character you mostly played" means.
    /// </summary>
    Task<SummaryActivity> GetActivity(int userId, DateTimeOffset from, DateTimeOffset to);

    Task<SummaryClogPage> GetNewCollectionLogItems(int characterId, DateTimeOffset from, DateTimeOffset to, int limit);

    Task<SummarySourcePage> GetSources(int characterId, DateTimeOffset from, DateTimeOffset to, int limit);

    /// <summary>
    /// Each item's best single receipt in [from, to) worth at least <paramref name="floor"/>, most
    /// valuable first.
    /// </summary>
    Task<IReadOnlyList<SummaryDrop>> GetNotableDrops(int characterId, DateTimeOffset from, DateTimeOffset to, long floor, int limit);
}
