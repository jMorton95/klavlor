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
    // The session's single biggest drop, and the feed tier it landed in (null when nothing in the
    // session cleared the feed's floor). Lets the panel wear the same tier colours as the feed.
    string? BestDropName,
    long BestDropValue,
    LootFeedTier? BestTier)
{
    /// <summary>Rolls per hour over the session's own span; 0 for an instantaneous single roll.</summary>
    public int RollsPerHour
    {
        get
        {
            var hours = (EndedAt - StartedAt).TotalHours;
            return hours <= 0.01 ? 0 : (int)Math.Round(Rolls / hours);
        }
    }
}
