namespace KlavLor.Application.Features.Loot.Feed;

public static class LootFeedGrouping
{
    public static readonly TimeSpan MaxGap = TimeSpan.FromHours(1);

    public static bool CanMerge(LootFeedEntry head, LootFeedEntry next) =>
        TryGetMergeDelta(head, next) is not null;

    public static TimeSpan? TryGetMergeDelta(LootFeedEntry head, LootFeedEntry next)
    {
        if (head.GroupKey != next.GroupKey) return null;
        var deltaFromOccurred = (head.OccurredAt - next.OccurredAt).Duration();
        var deltaFromAnchor = (head.GroupAnchorAt - next.OccurredAt).Duration();
        var nearest = deltaFromOccurred < deltaFromAnchor ? deltaFromOccurred : deltaFromAnchor;
        return nearest <= MaxGap ? nearest : null;
    }

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
            MaxKillCount = MaxNullable(head.MaxKillCount, next.MaxKillCount)
        };
    }

    private static int? MinNullable(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private static int? MaxNullable(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
