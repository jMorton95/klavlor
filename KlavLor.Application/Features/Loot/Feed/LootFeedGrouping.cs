using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Features.Loot.Feed;

public static class LootFeedGrouping
{
    // Site-wide session model (feed cards AND the SQL session/trend queries share these):
    //  - MaxGap (16h) is the outer cap — a session never spans more than this from its first kill.
    //  - SessionBreakGap (6h) splits a session at an *overnight* break: a gap of at least this much
    //    that lands on a different play-day than the kill it follows (e.g. play past midnight,
    //    sleep, resume next morning). A gap under 6h, or one within the same play-day, never splits.
    //  - PlayDayStart (06:00 local) is when a play-day rolls over, so a late-night run that crosses
    //    midnight still counts as one day. The SQL mirrors this with `- INTERVAL '6 hours'`.
    public static readonly TimeSpan MaxGap = TimeSpan.FromHours(16);
    public static readonly TimeSpan SessionBreakGap = TimeSpan.FromHours(6);
    public static readonly TimeSpan PlayDayStart = TimeSpan.FromHours(6);

    // Rare/Epic drops land far less often than Standard/Uncommon, so their feed cards use a
    // multi-day merge window — a grind toward a 1M+ drop keeps accumulating on one card across
    // days instead of fragmenting into near-empty cards. Feed-card grouping only; the SQL
    // session/trend queries stay on MaxGap.
    public static readonly TimeSpan WideMaxGap = TimeSpan.FromDays(3);

    public static TimeSpan MergeWindowFor(LootFeedTier tier) => tier switch
    {
        LootFeedTier.Rare or LootFeedTier.Epic => WideMaxGap,
        _ => MaxGap
    };

    // A single-kill session worth less than this is a "true one-off" (e.g. one Hill Giant) and is
    // hidden from the character session-history list as noise; multi-kill sessions are always kept.
    // Mirrors the feed's 10k interesting-drop floor (ILootFeedService.GetDropTier).
    public const long MinOneOffSessionValue = 10_000;

    // Fewer rolls than this at one source over the whole window is incidental, not an activity.
    public const int MinNotableRolls = 5;

    /// <summary>
    /// Whether a source is worth a row in the feed's recent-activity panel, judged on everything the
    /// character did there across the whole window rather than on one session.
    ///
    /// Both bars must clear, not either: volume without value is a walk through a slayer task, and
    /// value without volume is a single lucky kill that the swimlanes already show. Requiring both
    /// leaves the panel showing deliberate grinding, which is what it's for.
    ///
    /// Note this also hides a source visited only once or twice in two days even if it has uniques
    /// behind it — a single raid with poor loot no longer earns a row.
    /// </summary>
    public static bool IsNotableActivity(int rolls, long gp) =>
        rolls >= MinNotableRolls && gp >= MinOneOffSessionValue;

    public static bool CanMerge(LootFeedEntry head, LootFeedEntry next) =>
        TryGetMergeDelta(head, next) is not null;

    public static TimeSpan? TryGetMergeDelta(LootFeedEntry head, LootFeedEntry next)
    {
        if (head.GroupKey != next.GroupKey) return null;

        // Outer cap: the merged group's full span (earliest occurrence → latest) must fit inside
        // the tier's window — 16h for Standard/Uncommon/Legendary, multi-day for Rare/Epic.
        var window = MergeWindowFor(head.Tier);
        var start = head.GroupAnchorAt < next.OccurredAt ? head.GroupAnchorAt : next.OccurredAt;
        var end = head.OccurredAt > next.OccurredAt ? head.OccurredAt : next.OccurredAt;
        if (end - start > window) return null;

        // Distance to the group's nearest edge (its latest occurrence for a newer entry, its anchor
        // for an older one) — the gap this kill bridges.
        var deltaFromOccurred = (head.OccurredAt - next.OccurredAt).Duration();
        var deltaFromAnchor = (head.GroupAnchorAt - next.OccurredAt).Duration();
        var (nearest, nearestEdge) = deltaFromOccurred <= deltaFromAnchor
            ? (deltaFromOccurred, head.OccurredAt)
            : (deltaFromAnchor, head.GroupAnchorAt);

        // Overnight-break split: a gap of at least SessionBreakGap that crosses into a different
        // play-day starts a new session rather than merging. Skipped on the widened Rare/Epic
        // window — it exists precisely so a card can span sleep breaks; splitting overnight
        // would make the multi-day cap unreachable.
        if (window == MaxGap && nearest >= SessionBreakGap && PlayDayOf(next.OccurredAt) != PlayDayOf(nearestEdge))
            return null;

        return nearest;
    }

    // The Europe/London play-day an instant belongs to: the local calendar date after shifting the
    // boundary to PlayDayStart (06:00), so 02:00 counts as the previous day's late-night session.
    private static DateOnly PlayDayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime((IngestTimezone.ToZoneTime(instant) - PlayDayStart).DateTime);

    public static LootFeedEntry Merge(LootFeedEntry head, LootFeedEntry next)
    {
        var combinedDrops = new List<LootFeedDrop>(head.Drops.Count + next.Drops.Count);
        combinedDrops.AddRange(head.Drops);
        combinedDrops.AddRange(next.Drops);

        var headAnchor = head.GroupAnchorAt;
        var nextAnchor = next.GroupAnchorAt;
        var anchor = nextAnchor < headAnchor ? nextAnchor : headAnchor;
        var occurred = next.OccurredAt > head.OccurredAt ? next.OccurredAt : head.OccurredAt;

        return head with
        {
            Drops = combinedDrops,
            TotalValue = head.TotalValue + next.TotalValue,
            RunCount = head.RunCount + next.RunCount,
            OccurredAt = occurred,
            GroupStartedAt = anchor,
            MinKillCount = MinNullable(head.MinKillCount, next.MinKillCount),
            MaxKillCount = MaxNullable(head.MaxKillCount, next.MaxKillCount),
            MinKillOrdinal = MinNullable(head.MinKillOrdinal, next.MinKillOrdinal),
            MaxKillOrdinal = MaxNullable(head.MaxKillOrdinal, next.MaxKillOrdinal)
        };
    }

    private static int? MinNullable(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private static int? MaxNullable(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
