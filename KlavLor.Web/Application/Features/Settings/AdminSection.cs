namespace KlavLor.Web.Application.Features.Settings;

// One entry per admin section — the single source of truth for the nav, the routable page, the
// shell endpoint's slug validation and the section's own heading.
//
// The admin area used to be one page with thirteen collapsible sections and an anchor nav. That
// stopped scaling: nothing was linkable, the whole page's markup shipped on every visit, and a
// section was only findable by scrolling. It is now one section per URL — /admin/settings/{slug} —
// with the nav as real navigation. Adding a section is a row here plus a render branch in
// AdminSettingsShell.razor; nothing else changes, and no panel-body endpoint moved.
//
// Group is purely presentational: it breaks the nav into labelled runs so thirteen entries read as
// four short lists rather than one long one.
public sealed record AdminSection(string Slug, string NavLabel, string Title, string Group, string? Description = null);

public static class AdminSections
{
    public const string Operations = "Operations";
    public const string Data = "Data";
    public const string LuckMaths = "Luck maths";
    public const string Content = "Content";

    // Order here is the order in the nav. The first entry is what /admin/settings resolves to.
    public static readonly IReadOnlyList<AdminSection> All =
    [
        new("sync-status", "Sync status", "Sync status", Operations),
        new("jobs", "Background jobs", "Background jobs", Operations,
            "The most recent run of each background service — its outcome, how long it took and how many rows it processed. A red card is a failed or stuck job (still marked running long after it started). Expand any card for its recent run history."),
        new("management", "Management", "Management", Operations),
        new("icons", "Broken icons", "Broken icons", Operations,
            "Item and source icons that gave up after repeated failed wiki lookups (so they show no image anywhere). Retry clears the failure and the backfill re-attempts within a couple of minutes."),

        new("collection-log", "Collection log", "Collection log blacklist", Data,
            "Excluded items stop counting as collection-log drops everywhere — recent clogs, first-time feeds, activity counts and the live feed. Search to add; the list below shows what's currently excluded."),
        new("drop-rates", "Drop rates", "Drop rates", Data,
            "Manually fetch per-source drop rates from the wiki when some are missing. The list below shows sources with loot but no stored rates; search to re-fetch any source, or resync the whole backlog at once."),
        new("record-audit", "Record audit", "Record audit", Data,
            "Find and delete an individual sync record. RuneLite occasionally attributes a drop to the wrong source — opening a dossier at the moment something is equipped logs that item as loot from the dossier. Narrow to a character and source, page through the records, and remove just the bad one; deleting a whole character's loot to fix one row is not a repair."),
        new("sources", "Source names", "Source names", Data,
            "Rename or merge inconsistent loot-source names (e.g. variants/typos). Editing a name to one that already exists merges them. This repoints all loot and re-derives drop rates and icons — it can't be undone."),

        new("rate-modifiers", "Rate modifiers", "Source rate modifiers", LuckMaths,
            "Hand-correct a source whose stored wiki rates don't reflect real per-player odds (raids, Perilous Moons, and the like) by applying a multiplier to its expected kills-to-drop — for the whole source, or for a single item. This feeds both the character source page and the luck leaderboards."),
        new("baselines", "Baseline KC", "Baseline roll counts", LuckMaths,
            "Seed a character's kill count at a source for content they'd already ground before we had their data. It's added to counted kills, so once RuneLite starts logging them the count continues from the baseline."),
        new("delve-depths", "Delve depth", "Average delve depth", LuckMaths,
            "Doom of Mokhaiotl rolls its loot once per delve level, so luck has to be measured in delves rather than runs — but the loot itself never says how deep a run went. Set a character's real average here to make their Doom rates and luck accurate; anyone without an override is assumed to average "
            + KlavLor.Application.Features.Loot.DelveDepth.CharacterDelveDepthAdminHandler.DefaultDepth
            + " delves per run."),
        new("leaderboards", "Leaderboards", "Leaderboard exclusions", LuckMaths,
            "Hide a source from the luck leaderboards when its stored drop rates are wrong (e.g. shared rare-drop-table items). Excluded sources contribute no items to either the spoons or the dry-streak board."),

        new("item-values", "Item values", "Item values", Content,
            "Give an item a fixed intrinsic GP value. Untradeable pieces like the Noxious halberd's three components are worth millions each but have no Grand Exchange price, so RuneLite logs them at 0. A value set here becomes the truth site-wide, for past drops as well as future ones, and decides which feed tier they land in."),
        new("special-loot", "Special loot", "Special loot", Content,
            "Manually add an untradeable special drop — an Infernal Cape or Dizana's Quiver — that RuneLite never logs, to a character's log at a time you choose. It populates their collection log and always renders as a giga drop with the distinct spinning feed effect."),
    ];

    public static AdminSection Default => All[0];

    // Unknown or absent slug falls back to the first section rather than 404ing: the nav is the
    // only way in, so a bad slug means a stale bookmark, not a missing resource.
    public static AdminSection Resolve(string? slug) =>
        All.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase)) ?? Default;

    public static IEnumerable<IGrouping<string, AdminSection>> Grouped() => All.GroupBy(s => s.Group);
}
