using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.Application.Features.Loot.Log;

/// <summary>
/// A single character's loot for one Europe/London calendar day, rendered in the live-feed
/// card style. Summary fields cover every kill that day; <see cref="Entries"/> holds only the
/// valued (>=10K) kills, merged into runs and ordered newest-first.
/// </summary>
public sealed record CharacterDayFeed(
    DateOnly Day,
    int TotalKills,
    long TotalGp,
    int Sources,
    IReadOnlyList<LootFeedEntry> Entries);
