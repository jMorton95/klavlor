using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Feed;

/// <summary>
/// What every visible character has actually been doing over the last <see cref="WindowHours"/>,
/// grouped into play sessions. The live feed only ever shows individual drops that cleared a value
/// floor, so a 4,000-kill Lizardman Shaman grind that produced nothing over 10k is invisible there
/// despite being the day's real activity. This is the answer to "what has everyone been up to",
/// which drop cards alone can't give.
///
/// Trivial one-offs are filtered out by <see cref="LootFeedGrouping.IsNotableSession"/>.
/// </summary>
public sealed record RecentSessionsPanel(
    int WindowHours,
    IReadOnlyList<RecentSessionCharacter> Characters,
    // Sessions that were dropped as noise. Surfaced so "why isn't my kill in here" is answerable
    // without guessing at the rules.
    int FilteredOut);

public sealed record RecentSessionCharacter(
    int CharacterId,
    string CharacterName,
    int TotalRolls,
    long TotalGp,
    DateTimeOffset LastActiveAt,
    IReadOnlyList<RecentSession> Sessions);

public sealed record RecentSession(
    string SourceName,
    LootSourceType SourceType,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int Rolls,
    long Gp,
    // First-time collection-log receipts in this session — the thing most worth calling out.
    int ClogCount,
    // The feed tier of the session's biggest single drop, or null when nothing in it cleared the
    // feed's floor. Carried as a tier rather than the drop itself because the panel only uses it
    // for the row's edge colour, so it wears the same tier palette as the swimlanes.
    LootFeedTier? BestTier);
