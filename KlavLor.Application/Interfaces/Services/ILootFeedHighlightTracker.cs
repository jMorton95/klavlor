using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.Application.Interfaces.Services;

/// <summary>
/// Tracks the currently-crowned "biggest drop" card per (scope, tier). The crown is
/// sticky — it survives quiet days/months and only changes when a strictly bigger
/// drop lands within the tier's window:
///   Standard / Uncommon / Rare → rolling 7-day window
///   Epic / Legendary           → UTC calendar month (resets at midnight on the 1st)
/// </summary>
public interface ILootFeedHighlightTracker
{
    /// <summary>Returns true when this entry currently holds the crown for its (scope, tier).</summary>
    bool IsHighlight(LootFeedEntry entry);

    /// <summary>Human-readable label rendered on the trophy ribbon for a given tier, as of <paramref name="asOf"/>.</summary>
    string LabelFor(LootFeedTier tier, DateTimeOffset asOf);

    /// <summary>Seeds the crown for a partition from a snapshot of its buffer (startup priming).</summary>
    void SetInitial(LootFeedScope scope, LootFeedTier tier, IEnumerable<LootFeedEntry> bufferSnapshot);

    /// <summary>
    /// Recomputes the crown against the post-publish buffer. Returns a non-null change
    /// only when the crown moved — caller emits a demote + promote OOB pair on the SSE
    /// channel so the previous winner re-renders without its ribbon.
    /// </summary>
    HighlightChange? OnBufferChanged(LootFeedScope scope, LootFeedTier tier, IEnumerable<LootFeedEntry> bufferSnapshot);
}
