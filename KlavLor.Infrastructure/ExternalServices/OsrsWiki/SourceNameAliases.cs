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

            // The Gauntlet page hosts both variants' reward tables; section filter
            // disambiguates because Normal and Corrupted have different rarities for
            // the same items (e.g. Enhanced crystal weapon seed: 1/2000 vs 1/400).
            ["Crystalline Hunllef"] = new WikiPageMapping("The Gauntlet", "Normal"),
            ["Corrupted Hunllef"] = new WikiPageMapping("The Gauntlet", "Corrupted"),
        };

    public static WikiPageMapping Resolve(string sourceName) =>
        Aliases.TryGetValue(sourceName, out var m) ? m : new WikiPageMapping(sourceName);
}
