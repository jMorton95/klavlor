namespace KlavLor.Application.Features.Loot.Feed;

/// <summary>
/// One kill on the live roll ticker: who killed what, and which roll it was.
/// </summary>
/// <remarks>
/// Deliberately carries NO loot. The ticker is about activity, not value - which is exactly why it
/// can show what the swimlanes never do, a dry kill. It is also what makes the stream cheap: there
/// is no DropsJson to deserialise, no effective-price lookup, no tier to classify and no rate to
/// resolve, so a roll costs a fraction of what a feed card costs to publish.
/// </remarks>
/// <param name="KillOrdinal">
/// The roll number. RuneLite's own count where it supplied one, otherwise our chronological
/// position at that source plus any admin baseline - the same rule and the same resolver the feed
/// card uses, so the two can never quote different numbers for one kill. Null only when the record
/// has no character attached, which cannot happen for anything that reaches the ticker.
/// </param>
public sealed record LootRollEntry(
    string CharacterName,
    int? GameCharacterId,
    string SourceName,
    int? KillOrdinal,
    DateTimeOffset OccurredAt)
{
    /// <summary>
    /// Stable per roll, so a reconnect that replays the buffer cannot insert a duplicate: the
    /// ticker keys on it client-side. Record id would be simpler but is not in scope here, and the
    /// tuple below is unique per kill anyway.
    /// </summary>
    public string DomId { get; } =
        $"roll-{GameCharacterId ?? 0:x}-{OccurredAt.UtcTicks:x}-{KillOrdinal ?? 0:x}";
}

/// <summary>
/// One kill as the startup seed reads it, BEFORE its roll number is resolved.
/// </summary>
/// <remarks>
/// Separate from LootRollEntry because the ordinal is not known yet: it is filled by
/// ILootRecordRepository.GetKillOrdinals, which is the one place the roll-number rule lives. A
/// third spelling of that rule inside the seed query would be a fourth thing to keep in step, and
/// the ticker and the feed card would eventually disagree about the same kill.
/// </remarks>
public sealed record LootRollSeedRow(
    int RecordId,
    int GameCharacterId,
    string CharacterName,
    string SourceName,
    int? KillCount,
    DateTimeOffset OccurredAt);
