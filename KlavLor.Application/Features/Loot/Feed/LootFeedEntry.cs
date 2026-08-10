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
    // Rolls done at this source since the character's PREVIOUS receipt of this same item. Null on a
    // first-ever receipt, where the absolute kill count already is the right basis.
    //
    // This is the only honest denominator for a repeat drop. Judging one against its absolute roll
    // number said a second 1/100 item at kill 200 was "2x dry" when the player had actually gone 150
    // rolls since the last one, and made every guaranteed drop absurd — an Atlatl dart (1/1) from
    // the 300th Lunar Chest read as 300x dry.
    int? RollsSincePrevious = null);

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
}

public sealed record LootFeedBroadcast(LootFeedEntry Entry, string? PreviousDomId, HighlightChange? HighlightChange = null);

public sealed record HighlightChange(LootFeedEntry? Demoted, LootFeedEntry? Promoted);
