namespace KlavLor.Application.Features.Loot.ItemValues;

// A configured override, for the admin list. RecordCount is how many stored loot records currently
// contain the item, so the admin can see the blast radius of a change before making it.
public sealed record ItemValueOverrideRow(int ItemId, string ItemName, int Value, long RecordCount);

// A candidate item for the lookup box. Drawn from items that have actually been dropped, so the
// admin can only ever override something the site knows about. RawPrice is the price RuneLite last
// reported for it (0 for the untradeables this feature exists for).
public sealed record ItemValueCandidate(int ItemId, string ItemName, int RawPrice, long RecordCount, int? OverrideValue);
