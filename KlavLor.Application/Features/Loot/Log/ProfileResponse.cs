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

public sealed record SourceCollection(
    string SourceName,
    int CharacterKc,
    IReadOnlyList<CollectionEntry> Entries,
    IReadOnlyList<MissingClogItem> MissingItems,
    // Deepest delve reached at this source (max stored EffectiveKills); 0 for ordinary sources.
    // Feeds Doom's depth-aware luck maths.
    int CharacterDepth = 0);

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
    // per-source loot model (raid unique-table shares, multi-roll tables, etc.). Null when
    // there's no usable rate. The character page prefers this over the raw rolls×num/den.
    double? EffectiveKcPerDrop = null);

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
    // per-source loot model (raid unique-table shares, multi-roll tables, etc.). Null when
    // there's no usable rate. The character page prefers this over the raw rolls×num/den.
    double? EffectiveKcPerDrop = null);

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

