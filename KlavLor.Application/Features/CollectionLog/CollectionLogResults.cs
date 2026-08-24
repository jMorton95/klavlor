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
    // Deliberately never names the upstream on screen: which third party we read from is an
    // implementation detail, and "no collection log data" is what a viewer can actually act on.
    public string? Blocker => Outcome switch
    {
        CollectionLogSyncOutcome.Never => "No collection log data yet — it will appear after the next sync.",
        CollectionLogSyncOutcome.NotSynced or CollectionLogSyncOutcome.NotFound => "No collection log data for this character.",
        CollectionLogSyncOutcome.Failed => "The last refresh failed, so anything below may be out of date.",
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
    int Total,
    /// <summary>
    /// What to draw beside the name. A source icon when the category names a boss we already hold
    /// one for, otherwise a representative item from the category — only 12 of the 124 categories
    /// match a known source, so an item icon is the fallback that actually renders for the rest.
    /// </summary>
    CollectionLogIconKind IconKind = CollectionLogIconKind.None,
    string? IconName = null)
{
    public bool IsComplete => Total > 0 && Obtained >= Total;
    public bool IsStarted => Obtained > 0;
    public double Percent => Total > 0 ? Obtained * 100.0 / Total : 0;
}

public enum CollectionLogIconKind { None, Source, Item }

/// <summary>
/// Display order for the five upstream groups. The upstream returns them alphabetically, which puts
/// clues above raids; in-game and in players' heads raids sit with bosses at the top.
/// </summary>
public static class CollectionLogGroups
{
    private static readonly string[] Order = ["bosses", "raids", "clues", "minigames", "other"];

    public static int SortOrder(string groupName)
    {
        var index = Array.FindIndex(Order, g => string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? Order.Length : index;
    }
}

/// <summary>A recently obtained item, newest first — the "what just happened" strip.</summary>
public sealed record CollectionLogRecentUnlock(
    int ItemId,
    string Name,
    string? CategoryDisplayName,
    DateTimeOffset ObtainedAt,
    /// <summary>Same attribution as CollectionLogItemState.FirstReceipt; null when untracked.</summary>
    CollectionLogFirstReceipt? FirstReceipt = null);

/// <summary>
/// Where and on which roll an item first arrived, from OUR loot data. Null throughout when we hold
/// no drop for it — the normal case for anything obtained before tracking began.
/// </summary>
public sealed record CollectionLogFirstReceipt(string SourceName, int? KillCount);

/// <summary>One item within a category, with whether this character has it.</summary>
public sealed record CollectionLogItemState(
    int ItemId,
    string Name,
    bool Obtained,
    int Count,
    DateTimeOffset? ObtainedAt,
    /// <summary>
    /// The roll it landed on, attributed to the source that actually dropped it. Comes from our own
    /// loot records, not the collection log, which knows nothing about kill counts — so it is null
    /// for anything we never tracked, and must simply be omitted rather than shown as zero.
    /// </summary>
    CollectionLogFirstReceipt? FirstReceipt = null);

/// <summary>One category's items for one character, plus what the panel needs to title itself.</summary>
public sealed record CollectionLogCategoryView(
    string Slug,
    string DisplayName,
    CollectionLogIconKind IconKind,
    string? IconName,
    IReadOnlyList<CollectionLogItemState> Items);

/// <summary>A character's whole log: the header, its categories, and one category's items.</summary>
public sealed record CharacterCollectionLog(
    int GameCharacterId,
    string CharacterName,
    int Obtained,
    int Available,
    int GameMode,
    int? HiscoresRank,
    CollectionLogFreshness Freshness,
    IReadOnlyList<CollectionLogCategoryProgress> Categories,
    IReadOnlyList<CollectionLogRecentUnlock> RecentUnlocks);

/// <summary>One source a character has rolled, in the context of chasing a particular item.</summary>
public sealed record CollectionLogRollSource(string SourceName, int Rolls, bool DroppedIt);

/// <summary>One character's holding of a single item — powers the per-item comparison.</summary>
public sealed record CollectionLogItemHolder(
    int GameCharacterId,
    string CharacterName,
    bool Obtained,
    int Count,
    DateTimeOffset? ObtainedAt,
    /// <summary>
    /// Rolls from OUR loot data, and what they mean depends on whether they already have it.
    ///
    /// If they DO: only the source that actually dropped it to them. An item can come from several
    /// sources, so attributing a receipt to the wrong one misstates the grind — an Abyssal whip
    /// from an Abyssal demon is not a Sire drop, even though the Sire also drops it.
    ///
    /// If they DON'T: their biggest few sources among everything that drops it, because the
    /// interesting figure while chasing is where the rolls have actually gone.
    ///
    /// Empty when we hold no loot data at all for them at any relevant source — the normal case for
    /// anything obtained before tracking began. Empty must render as "unknown", never as zero.
    /// </summary>
    IReadOnlyList<CollectionLogRollSource> RollSources)
{
    public int TotalRolls => RollSources.Sum(r => r.Rolls);
    public bool HasRollData => RollSources.Count > 0;
}

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
    /// <summary>
    /// Item id → how many of it they hold. A dictionary rather than a set because the comparison
    /// shows quantities: two characters both "having" a Bandos hilt is not the same story when one
    /// has eleven of them.
    /// </summary>
    IReadOnlyDictionary<int, int> Counts,
    /// <summary>
    /// Item id → where and on which roll it first arrived FOR THIS CHARACTER, from our own loot
    /// records. The comparison is about whose grind was worse, and a tick alone cannot say that: two
    /// characters both holding a Bandos hilt is a different story when one got it on roll 40 and the
    /// other on roll 900. Absent for anything we never tracked, which must stay blank rather than
    /// read as roll zero.
    /// </summary>
    IReadOnlyDictionary<int, CollectionLogFirstReceipt>? FirstReceipts = null)
{
    public bool Has(int itemId) => Counts.ContainsKey(itemId);
    public int CountOf(int itemId) => Counts.TryGetValue(itemId, out var n) ? n : 0;

    public CollectionLogFirstReceipt? ReceiptOf(int itemId) =>
        FirstReceipts is not null && FirstReceipts.TryGetValue(itemId, out var receipt) ? receipt : null;
}

/// <summary>A row in the item search, with how many characters hold it.</summary>
public sealed record CollectionLogSearchRow(
    int ItemId,
    string Name,
    IReadOnlyList<string> Categories,
    int ObtainedBy,
    int TotalCharacters);
