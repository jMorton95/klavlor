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
    string? EffectiveRarity = null);

public sealed record LootFeedBroadcast(LootFeedEntry Entry, string? PreviousDomId, HighlightChange? HighlightChange = null);

public sealed record HighlightChange(LootFeedEntry? Demoted, LootFeedEntry? Promoted);
