namespace KlavLor.Application.Features.Loot.SourceModels;

// Raids (Chambers of Xeric, Tombs of Amascut, Theatre of Blood) whose chest lists each unique
// as its SHARE of the unique table (numerator/denominator where the denominator is the table's
// total weight), NOT a per-raid probability. The wiki number answers "given a unique dropped,
// which item is it" — so a CoX twisted bow shown as 2/60 is ~3% of uniques, not ~1 in 30 raids.
// The real per-player expected count multiplies that share by the average number of completions
// per unique a single player receives, which folds in the points-based unique frequency (and, for
// ToB, the party-size split of a single team roll).
//
// IDENTIFYING A SHARE. A share is recognised by the ITEM being on the raid's unique list — the
// names below — and, independently, by its denominator matching a known table weight. Either is
// sufficient; both are declared because each covers the other's failure mode:
//
//   - The item list survives the wiki restructuring its table. It did exactly that in August 2026:
//     the CoX shares moved from x/69 to x/60 (normal) and x/56 (challenge mode), the denominator
//     match silently stopped firing, and every CoX unique reverted to reading as its raw share — a
//     twisted bow as "1/30 raids" instead of ~1/960, on the leaderboard and every rate column.
//     Nothing in our code changed; the hourly drop-rate sync simply picked the new numbers up.
//   - The denominator list survives an item being RENAMED or a new unique being added to an
//     existing table, which the name list alone would miss just as silently.
//
// Everything else passes through unscaled: tertiary rolls (pets, dust, thread, kits) use their own
// denominator and are already per-completion.
//
// Item names are matched case-insensitively because the three vocabularies that reach here disagree
// on case — the wiki's drop rows, RuneLite's drop names, and the collection log's item names (which
// spell it "Scythe of vitur (uncharged)").
//
// These sources DO appear on the leaderboard — unlike Doom, their luck is computable, it just
// needs this normalisation.
public abstract class RaidUniqueShareStrategy(
    string sourceName, double completionsPerUnique, string[] uniqueItems, int[] tableDenominators)
    : SourceLootStrategy(sourceName)
{
    private readonly HashSet<string> _uniqueItems = new(uniqueItems, StringComparer.OrdinalIgnoreCase);

    // The raid's unique-table items, for tests and for anything that needs to cross-check the list
    // against what the wiki currently publishes.
    public IReadOnlySet<string> UniqueItems => _uniqueItems;

    // A raid claim is one completion.
    public override int EffectiveKills(IReadOnlyList<ClaimDrop> drops) => 1;

    public override double ExpectedCompletions(string? itemName, int numerator, int denominator, int rolls)
    {
        var flat = base.ExpectedCompletions(itemName, numerator, denominator, rolls);
        // Keep the "no usable rate" sentinel a sentinel — multiplying it would make it look finite.
        if (flat >= double.MaxValue) return flat;
        return IsUniqueTableShare(itemName, denominator) ? flat * completionsPerUnique : flat;
    }

    private bool IsUniqueTableShare(string? itemName, int denominator) =>
        (itemName is not null && _uniqueItems.Contains(itemName)) || tableDenominators.Contains(denominator);
}

// ~1 unique per 30–33 raids (solo normal). Share denominators are 60 (normal) and 56 (challenge
// mode); 69 was the pre-August-2026 table and is kept because stored rows may predate a resync.
public sealed class ChambersOfXericStrategy() : RaidUniqueShareStrategy(
    "Chambers of Xeric", 32,
    [
        "Dexterous prayer scroll", "Arcane prayer scroll", "Twisted buckler", "Dragon hunter crossbow",
        "Dinh's bulwark", "Ancestral hat", "Ancestral robe top", "Ancestral robe bottom",
        "Dragon claws", "Elder maul", "Kodai insignia", "Twisted bow"
    ],
    [69, 60, 56]);

// ~1 unique per 21 raids (~RL300 solo); the share denominator is 24.
public sealed class TombsOfAmascutStrategy() : RaidUniqueShareStrategy(
    "Tombs of Amascut", 21,
    [
        "Osmumten's fang", "Tumeken's shadow (uncharged)", "Elidinis' ward", "Lightbearer",
        "Masori mask", "Masori body", "Masori chaps"
    ],
    [24]);

// Team unique chance ~1/9.1 handed to one of ~4 players, so a given player's factor is
// ~9.1 × 4 ≈ 36. Share denominators are 19 (normal) and 18 (hard mode).
public sealed class TheatreOfBloodStrategy() : RaidUniqueShareStrategy(
    "Theatre of Blood", 36,
    [
        "Avernic defender hilt", "Ghrazi rapier", "Sanguinesti staff (uncharged)",
        "Justiciar faceguard", "Justiciar chestguard", "Justiciar legguards",
        "Scythe of Vitur (uncharged)"
    ],
    [19, 18]);
