namespace KlavLor.Application.Interfaces.Services;

// One stored modifier row, flattened for the cache (empty ItemName = source-wide).
public readonly record struct SourceRateModifierValue(string SourceName, string ItemName, double Multiplier);

// Singleton in-memory cache of the admin-configured per-source (and per-item) rate multipliers.
// Reads are on the hot path (every leaderboard entry, every character-page item), writes are
// rare (admin edit), so it holds an immutable snapshot swapped atomically on Replace.
public interface ISourceRateModifierCache
{
    // Item-specific modifier if one exists, else the source-wide modifier, else 1.0 (no change).
    double GetMultiplier(string sourceName, string? itemName);

    void Replace(IEnumerable<SourceRateModifierValue> modifiers);
}
