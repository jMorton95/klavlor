using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Log;

public enum HeatmapMode
{
    Gp,
    Clogs
}

public sealed record ProfileHeader(
    int CharacterId,
    string CharacterName,
    string UserName,
    DateTimeOffset? FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    int TotalSources,
    long TotalKills,
    long TotalGp);

public sealed record ProfileWindowStats(
    WindowStats Last7d,
    WindowStats Last30d,
    WindowStats AllTime);

public sealed record WindowStats(
    long Kills,
    long Gp,
    long GpPerHour,
    int NewItems,
    double ActiveHours);

public sealed record HeatmapData(
    DateOnly From,
    DateOnly To,
    HeatmapMode Mode,
    IReadOnlyList<DayBucket> Days);

public sealed record DayBucket(
    DateOnly Day,
    int Kills,
    long Gp,
    int Clogs = 0);

public sealed record MonthlyTrend(
    DateOnly From,
    DateOnly To,
    string Range,
    IReadOnlyList<MonthBucket> Months);

public sealed record MonthBucket(
    int Year,
    int Month,
    int Kills,
    long Gp,
    int Clogs,
    IReadOnlyList<MonthSegment> TopSegments);

public sealed record MonthSegment(
    string ItemName,
    string SourceName,
    long Value);

/// <summary>
/// Month-by-month roll counts, stacked by source — the companion to <see cref="MonthlyTrend"/>,
/// which answers "what was it worth". A roll is one logged kill or claim, i.e. one turn of a
/// source's drop table, so this reads as how much grinding actually happened and at what. Value
/// and volume diverge sharply (a single 1B drop outweighs 40k Lizardman Shaman kills), so neither
/// chart substitutes for the other.
/// </summary>
public sealed record MonthlyRollTrend(
    DateOnly From,
    DateOnly To,
    string Range,
    IReadOnlyList<RollMonthBucket> Months);

/// <summary>
/// One month's rolls. <see cref="Rolls"/> is the month's true total across every source; TopSources
/// is capped, so its sum can be lower and the remainder belongs in an "Other" segment.
/// </summary>
public sealed record RollMonthBucket(
    int Year,
    int Month,
    int Rolls,
    long Gp,
    IReadOnlyList<RollSourceSegment> TopSources);

public sealed record RollSourceSegment(
    string SourceName,
    int Rolls,
    long Gp);

public sealed record PersonalRecords(
    LootKillEntry? BiggestKill,
    string? BiggestKillSource,
    DayBucket? BiggestDay,
    BestHour? BestHour,
    TopSource? TopKcSource,
    BiggestItem? BiggestItem);

public sealed record BestHour(
    DateTimeOffset WindowStart,
    long Gp,
    int Kills);

public sealed record TopSource(
    string SourceName,
    LootSourceType SourceType,
    int Kills,
    long Gp);

public sealed record BiggestItem(
    string ItemName,
    int Quantity,
    long Value,
    string SourceName,
    DateTimeOffset OccurredAt);

/// One actual claim at a depth-modelled source (e.g. one Doom run), carrying the depth that
/// claim's loot proves it reached. Ordered oldest-first. Empty for ordinary sources.
/// One actual claim at a depth-modelled source (e.g. one Doom run). Depth is the stored
/// EffectiveKills where the backfill has derived it, otherwise 0 — in which case DropsJson lets the
/// handler derive it on read, so the page is correct even before the backfill has run. Ordered
/// oldest-first. Empty for sources with no depth model.
public sealed record SourceRun(int RecordId, DateTimeOffset OccurredAt, int Depth, string? DropsJson = null);

public sealed record SourceCollection(
    string SourceName,
    int CharacterKc,
    IReadOnlyList<CollectionEntry> Entries,
    IReadOnlyList<MissingClogItem> MissingItems,
    // Every run at this source with a derived depth, oldest first. Depth-modelled sources
    // (Doom) compute expected KC from these ACTUAL per-run depths — never from a single
    // max-ever depth, which would assume every run went as deep as the best one and make
    // everyone look drier than they are. Empty for ordinary flat-rate sources.
    IReadOnlyList<SourceRun> Runs)
{
    /// <summary>
    /// Total delve levels cleared across every run, for depth-modelled sources; 0 for ordinary ones.
    /// This - not CharacterKc - is what a Doom luck figure must be measured against, because the
    /// rates are per delve level and one run can be four delves or twenty.
    /// </summary>
    // Computed, NOT an initialised auto-property: `with { Runs = [] }` copies backing fields and
    // does not re-run initialisers, so an initialised TotalDelves kept its old value and left raids
    // reporting "520 delves across 520 runs" after Runs had been cleared.
    public int TotalDelves => Runs.Sum(r => r.Depth);

    /// <summary>Whether this source's odds are modelled per delve rather than per kill.</summary>
    public bool IsDepthModelled => TotalDelves > 0;

    /// <summary>The figure luck is judged against: delves where we model depth, else kills.</summary>
    /// <summary>
    /// The figure luck is judged against: RUNS, always. Depth-modelled rates are expressed per run
    /// (see DoomLootStrategy.ExpectedCompletionsForRuns), so no conversion is needed — and a
    /// per-delve basis would need a different denominator for every item, since each unique becomes
    /// eligible at a different level.
    /// </summary>
    public int LuckObserved => CharacterKc;

    /// <summary>
    /// Delves credited per run. Every run carries the same depth (the assumption, or the admin
    /// override), so this converts any run-denominated number — a drop's kill count, for instance —
    /// onto the delve scale the rates are expressed in. 1 for sources with no depth model.
    /// </summary>
    public int DelvesPerRun => IsDepthModelled && Runs.Count > 0
        ? Math.Max(1, TotalDelves / Runs.Count)
        : 1;
}

/// <summary>
/// Monthly kill activity for one character at one source — drives the character source
/// page's kill-history charts (monthly bars + cumulative line). Months with no kills are
/// not included; the panel densifies the timeline itself.
/// </summary>
public sealed record SourceKillTrend(
    string SourceName,
    IReadOnlyList<SourceKillTrendMonth> Months);

public sealed record SourceKillTrendMonth(
    int Year,
    int Month,
    int Kills,
    long Value);

public sealed record MissingClogItem(
    string ItemName,
    string? Rarity = null,
    int? RarityNumerator = null,
    int? RarityDenominator = null,
    int Rolls = 1,
    // Expected kills-to-first-drop for this item at this source, normalised through the
    // per-source loot model (raid unique-table shares, multi-roll tables, depth models) and
    // scaled by any admin rate modifier. Null when there's no usable rate. This — not the raw
    // stored Rarity — is what every luck surface must use.
    double? EffectiveKcPerDrop = null,
    // Display form of EffectiveKcPerDrop, e.g. "1/540". Differs from Rarity whenever a source
    // model or an admin rate modifier applies, and is populated for depth-modelled sources that
    // have no stored Rarity at all. Rate columns must render this in preference to Rarity.
    string? EffectiveRarity = null);

/// <summary>
/// Compact source overview rendered into the hover popover on a feed card.
/// Per character+source: KC, total GP, collection-log progress (X of Y),
/// and the five biggest drops they've ever received from this source.
/// </summary>
public sealed record SourcePopoverData(
    string SourceName,
    int KillCount,
    long TotalGp,
    int ClogUnlocked,
    int ClogTotal,
    IReadOnlyList<LootDropSummary> TopDrops);

public sealed record CollectionEntry(
    string ItemName,
    DateTimeOffset FirstReceivedAt,
    DateTimeOffset LastReceivedAt,
    int TotalDrops,
    long TotalQuantity,
    long TotalValue,
    bool MarkedFirstTime,
    int? KillCount,
    int? KillOrdinal,
    int? LastKillCount,
    int? LastKillOrdinal,
    string? Rarity = null,
    int? RarityNumerator = null,
    int? RarityDenominator = null,
    int Rolls = 1,
    IReadOnlyList<DropEvent>? DropEvents = null,
    // Expected kills-to-first-drop for this item at this source, normalised through the
    // per-source loot model (raid unique-table shares, multi-roll tables, depth models) and
    // scaled by any admin rate modifier. Null when there's no usable rate. This — not the raw
    // stored Rarity — is what every luck surface must use.
    double? EffectiveKcPerDrop = null,
    // Display form of EffectiveKcPerDrop, e.g. "1/540". See MissingClogItem.EffectiveRarity.
    string? EffectiveRarity = null,
    // Id of the LootRecord this item first dropped on. Used to window SourceCollection.Runs to
    // the runs that happened up to (and including) the first receipt, so a depth-modelled
    // item's luck is judged against the depths actually delved before it dropped.
    int FirstRecordId = 0);

public sealed record DropEvent(DateTimeOffset OccurredAt, int? KillCount, int? KillOrdinal);

public sealed record FirstTimeFeed(
    IReadOnlyList<FirstTimeEntry> Entries,
    DateTimeOffset? NextBefore,
    bool HasMore);

public sealed record FirstTimeEntry(
    DateTimeOffset OccurredAt,
    string SourceName,
    LootSourceType SourceType,
    string ItemName,
    int Quantity,
    long Value,
    int? KillCount,
    int? KillOrdinal);

public sealed record TopItemsList(
    IReadOnlyList<TopItem> Items);

public sealed record TopItem(
    string ItemName,
    long TotalQuantity,
    long TotalValue,
    int SourceCount,
    string TopSourceName,
    DateTimeOffset FirstReceivedAt,
    bool EverFirstTime);

