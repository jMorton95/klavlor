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
    DateTimeOffset? OccurredAt = null);

public sealed record LootFeedBroadcast(LootFeedEntry Entry, string? PreviousDomId, HighlightChange? HighlightChange = null);

public sealed record HighlightChange(LootFeedEntry? Demoted, LootFeedEntry? Promoted);
