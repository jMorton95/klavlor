using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Feed;

public sealed record LootFeedEntry(
    string UserName,
    int UserId,
    string SourceName,
    LootSourceType SourceType,
    long TotalValue,
    List<LootFeedDrop> Drops,
    DateTimeOffset OccurredAt,
    LootFeedTier Tier,
    string? CharacterName = null,
    int? GameCharacterId = null,
    int RunCount = 1,
    DateTimeOffset? GroupStartedAt = null,
    int? MinKillCount = null,
    int? MaxKillCount = null,
    int? MinKillOrdinal = null,
    int? MaxKillOrdinal = null,
    LootFeedScope Scope = LootFeedScope.Main,
    // Derived depth of the run this card represents, for depth-modelled sources (Doom's delve
    // level); null for ordinary sources. Feeds the per-drop effective rate so a card is rated
    // against the run that actually produced the drop, not a whole-history average.
    int? RunDepth = null)
{
    public DateTimeOffset GroupAnchorAt => GroupStartedAt ?? OccurredAt;

    public string GroupKey => $"{(int)SourceType}|{SourceName}|{UserId}|{GameCharacterId ?? 0}";

    public string DomId => $"loot-feed-entry-{StableHash(GroupKey):x8}-{OccurredAt.UtcTicks:x}";

    private static uint StableHash(string s)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (var c in s)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}

public sealed record LootFeedDrop(
    string Name,
    int Quantity,
    int Price,
    bool IsFirstTime = false,
    bool IsCollectionLogItem = false,
    bool IsSpecial = false,
    // Effective expected KC for this item at this source, straight from SourceLootService — so it
    // already carries the source's loot model (raid unique shares, Doom's per-run delve depth) and
    // any admin rate modifier. Null when there is no usable rate. Paired with EffectiveRarity for
    // display so a feed card can state how lucky the drop was, using the same numbers as the
    // character page and the leaderboard.
    double? ExpectedKc = null,
    string? EffectiveRarity = null,
    // The roll this drop actually landed on, held per drop rather than per card because a card
    // covers a whole session: judging a drop against the card's LATEST count meant a Venator fang
    // that came on rate at roll 20 drifted to "6.7x dry" purely because the player kept going.
    //
    // KillCount is RuneLite's reported figure and is null when it didn't send one — which is the
    // common case for chest-style sources. KillOrdinal is then this record's own chronological
    // position, resolved per drop. Without it every drop on such a card fell back to the CARD's
    // first ordinal, so four Lunar Chest uniques spread across rolls 197-420 all claimed roll 197
    // and read as equally lucky.
    int? KillCount = null,
    int? KillOrdinal = null,
    // When this specific drop happened — distinct from the card's OccurredAt, which is the whole
    // group's latest. Drives the per-drop ordinal lookup for records with no reported count.
    DateTimeOffset? OccurredAt = null,
    // Admin decision (LootRecord.ExcludedFromLuck) that this receipt must inform nobody's luck.
    // The drop still shows on the card with its value and tier; only the lucky/dry line goes.
    bool ExcludedFromLuck = false);

public static class FeedLuckRules
{
    // A drop only gets a lucky/dry verdict when it is rare enough for one to mean anything: 1 in 6
    // or rarer. Below that, normal variance reads as a dramatic multiple and a guaranteed 1/1 drop
    // reports its whole kill count as dryness.
    //
    // Measured as EXPECTED ROLLS from SourceLootService, never as the raw stored denominator. That
    // distinction is what keeps raid uniques on the board: Chambers of Xeric lists a prayer scroll
    // as 20/69, which looks like 1 in 3.45 and would be filtered out, but the stored figure is a
    // share of the unique table — its real expectation is ~110 raids. RaidUniqueShareStrategy has
    // already applied that scaling by the time we see ExpectedKc, so the three raids need no
    // special case here.
    public const double MinExpectedRolls = 6.0;

    public static bool WorthRating(double? expectedKc) => expectedKc is { } kc && kc >= MinExpectedRolls;

    /// <summary>
    /// Whether a feed card should state how lucky this drop was at all. Every reason to stay silent
    /// lives here rather than in the card's markup, so the policy is one testable decision:
    ///
    /// <list type="bullet">
    /// <item>Not a collection-log item — a snapdragon seed has a drop rate but nobody tracks their
    /// luck on it, and a line on every common drop buries the interesting ones.</item>
    /// <item>Not the character's FIRST receipt of the item. A luck figure answers "how long did this
    /// take to arrive", which is a question a first receipt has an answer to and a repeat does not:
    /// the same item four times over is four numbers about the same slot, and the interesting one is
    /// the first. Repeats keep their value, tier, roll number and rate — they just make no claim
    /// about luck. IsFirstTime is the flag the ingest already maintains for the first-time feed and
    /// the card's own badge, so the two can never disagree.</item>
    /// <item>Excluded by an admin at the record level, for a receipt we cannot rate honestly.</item>
    /// <item>No usable rate for this item at this source, or one too common to be worth a verdict
    /// (<see cref="WorthRating"/>).</item>
    /// </list>
    /// </summary>
    public static bool ShouldRate(LootFeedDrop drop) =>
        drop.IsCollectionLogItem
        && drop.IsFirstTime
        && !drop.ExcludedFromLuck
        && drop.ExpectedKc is > 0
        && !string.IsNullOrEmpty(drop.EffectiveRarity)
        && WorthRating(drop.ExpectedKc);
}

public sealed record LootFeedBroadcast(LootFeedEntry Entry, string? PreviousDomId, HighlightChange? HighlightChange = null);

public sealed record HighlightChange(LootFeedEntry? Demoted, LootFeedEntry? Promoted);
