using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Feed;

public sealed record LootFeedEntry(
    string UserName,
    string SourceName,
    LootSourceType SourceType,
    long TotalValue,
    List<LootFeedDrop> Drops,
    DateTimeOffset OccurredAt);

public sealed record LootFeedDrop(string Name, int Quantity, int Price);
