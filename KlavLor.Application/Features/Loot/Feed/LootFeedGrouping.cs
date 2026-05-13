namespace KlavLor.Application.Features.Loot.Feed;

public static class LootFeedGrouping
{
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
            GroupStartedAt = anchor
        };
    }
}
