using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.CollectionLog;

// Read models for the Collection Log area. Everything here is sourced from the Temple-synced
// tables; nothing in this file carries a kill count or a luck figure, because Temple has no notion
// of either. Where a surface wants those it joins our own loot data separately — see
// CollectionLogHandler for why the two are deliberately kept apart.

/// <summary>
/// How fresh a character's log is, and whether it can be rendered at all. Every view checks this
/// before drawing, so "never synced" and "synced but stale" are visibly different states rather
/// than both showing as an empty page.
/// </summary>
public sealed record CollectionLogFreshness(
    CollectionLogSyncOutcome Outcome,
    DateTimeOffset? PlayerSyncedAt,
    DateTimeOffset? OurSyncedAt,
    string? Error)
{
    public bool HasData => Outcome is CollectionLogSyncOutcome.Ok or CollectionLogSyncOutcome.Unchanged;

    /// <summary>The player hasn't pressed sync on Temple in a while, so their log may be behind.</summary>
    public bool IsStale => PlayerSyncedAt is { } t && DateTimeOffset.UtcNow - t > TimeSpan.FromDays(7);

    /// <summary>A short reason a view can show instead of an empty grid.</summary>
    public string? Blocker => Outcome switch
    {
        CollectionLogSyncOutcome.Never => "Not synced yet — this character's log will appear after the next sync.",
        CollectionLogSyncOutcome.NotSynced => "This player hasn't synced their collection log to TempleOSRS yet.",
        CollectionLogSyncOutcome.NotFound => "TempleOSRS has no player with this character's name.",
        CollectionLogSyncOutcome.Failed => "The last sync failed. The data below may be out of date.",
        _ => null
    };
}

/// <summary>One character on the clan board. Straight off the state row — no aggregation.</summary>
public sealed record CollectionLogStanding(
    int GameCharacterId,
    string CharacterName,
    string UserName,
    int GameMode,
    int Obtained,
    int Available,
    int CategoriesFinished,
    int CategoriesAvailable,
    int? HiscoresRank,
    CollectionLogFreshness Freshness)
{
    public double Percent => Available > 0 ? Obtained * 100.0 / Available : 0;
}

/// <summary>Per-category progress for one character.</summary>
public sealed record CollectionLogCategoryProgress(
    string Slug,
    string DisplayName,
    string GroupName,
    int Obtained,
    int Total)
{
    public bool IsComplete => Total > 0 && Obtained >= Total;
    public double Percent => Total > 0 ? Obtained * 100.0 / Total : 0;
}

/// <summary>One item within a category, with whether this character has it.</summary>
public sealed record CollectionLogItemState(
    int ItemId,
    string Name,
    bool Obtained,
    int Count,
    DateTimeOffset? ObtainedAt);

/// <summary>A character's whole log: the header, its categories, and one category's items.</summary>
public sealed record CharacterCollectionLog(
    int GameCharacterId,
    string CharacterName,
    int Obtained,
    int Available,
    int GameMode,
    int? HiscoresRank,
    CollectionLogFreshness Freshness,
    IReadOnlyList<CollectionLogCategoryProgress> Categories);

/// <summary>One character's holding of a single item — powers the per-item comparison.</summary>
public sealed record CollectionLogItemHolder(
    int GameCharacterId,
    string CharacterName,
    bool Obtained,
    int Count,
    DateTimeOffset? ObtainedAt,
    /// <summary>
    /// Rolls at the source that drops it, from OUR loot data. Null when we hold no drops for this
    /// character at that source, which is the normal case for anything obtained before tracking
    /// started. A null must render as "unknown", never as zero.
    /// </summary>
    int? OurKillCount);

/// <summary>An item across the roster: what it is, and who has it.</summary>
public sealed record CollectionLogItemComparison(
    int ItemId,
    string Name,
    IReadOnlyList<string> Categories,
    IReadOnlyList<CollectionLogItemHolder> Holders)
{
    public int ObtainedBy => Holders.Count(h => h.Obtained);
}

/// <summary>One category compared across the roster.</summary>
public sealed record CollectionLogCategoryComparison(
    string Slug,
    string DisplayName,
    string GroupName,
    int Total,
    IReadOnlyList<CollectionLogItemState> Items,
    IReadOnlyList<CollectionLogCategoryStanding> Standings);

public sealed record CollectionLogCategoryStanding(
    int GameCharacterId,
    string CharacterName,
    int Obtained,
    int Total,
    IReadOnlySet<int> ObtainedItemIds);

/// <summary>A row in the item search, with how many characters hold it.</summary>
public sealed record CollectionLogSearchRow(
    int ItemId,
    string Name,
    IReadOnlyList<string> Categories,
    int ObtainedBy,
    int TotalCharacters);
