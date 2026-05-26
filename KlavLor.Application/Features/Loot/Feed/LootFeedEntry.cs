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
    int? MaxKillCount = null)
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

public sealed record LootFeedDrop(string Name, int Quantity, int Price);

public sealed record LootFeedBroadcast(LootFeedEntry Entry, string? PreviousDomId);
