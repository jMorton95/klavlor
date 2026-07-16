namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

/// <summary>
/// Wiki-page mapping for a RuneLite-reported source name. <see cref="SectionFilter"/>,
/// when non-null, restricts the parsed drop rows to those whose section heading
/// contains the substring (case-insensitive). Used when a single wiki page hosts
/// rates for multiple in-game NPCs — e.g. The Gauntlet covers both Crystalline and
/// Corrupted Hunllef in separate sub-sections with different rarities.
/// </summary>
public sealed record WikiPageMapping(string PageTitle, string? SectionFilter = null);

/// <summary>
/// Maps a RuneLite-reported source name (matching <c>LootRecord.SourceName</c>) to the
/// wiki page that hosts that source's drop tables. Most sources map 1:1 to a page with
/// the same name; this dictionary only covers the cases where they diverge or share.
/// </summary>
public static class SourceNameAliases
{
    private static readonly Dictionary<string, WikiPageMapping> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Single-monster pages that live under the encounter's name on the wiki.
            ["TzKal-Zuk"] = new WikiPageMapping("Inferno"),
            ["TzTok-Jad"] = new WikiPageMapping("TzHaar Fight Cave"),

            // Reward-chest / raid sources: the Bucket `dropsline` data lives on the chest's own
            // page, not the encounter/NPC page, so these must be mapped explicitly. A key that
            // doesn't match a real RuneLite source name is simply a no-op (the source falls back
            // to its own name → no rows → flagged in the missing-rates backlog), so mapping these
            // can only add coverage. Exact RuneLite source names for raids should be confirmed
            // against real loot history (see DropRateMiss backlog) — the base names are covered
            // here; mode variants (Hard/Expert/Challenge) map to the same chest pages.

            // The Gauntlet reward chest hosts both variants; the section filter disambiguates
            // because Regular and Corrupted have different rarities for the same items
            // (e.g. Enhanced crystal weapon seed: 1/2000 vs 1/400). The variant is encoded as a
            // "#Regular"/"#Corrupted" anchor on the drop's "Dropped from", surfaced as Section.
            ["Crystalline Hunllef"] = new WikiPageMapping("Reward Chest (The Gauntlet)", "Regular"),
            ["Corrupted Hunllef"] = new WikiPageMapping("Reward Chest (The Gauntlet)", "Corrupted"),

            // Raid reward chests.
            ["Chambers of Xeric"] = new WikiPageMapping("Ancient chest"),
            ["Chambers of Xeric Challenge Mode"] = new WikiPageMapping("Ancient chest"),
            ["Theatre of Blood"] = new WikiPageMapping("Monumental chest"),
            ["Theatre of Blood Hard Mode"] = new WikiPageMapping("Monumental chest"),
            ["Tombs of Amascut"] = new WikiPageMapping("Chest (Tombs of Amascut)"),
            ["Tombs of Amascut Expert Mode"] = new WikiPageMapping("Chest (Tombs of Amascut)"),
        };

    public static WikiPageMapping Resolve(string sourceName) =>
        Aliases.TryGetValue(sourceName, out var m) ? m : new WikiPageMapping(sourceName);
}
