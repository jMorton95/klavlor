using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Services;

// Ids and names live in ONE snapshot so a Replace can never be observed half-applied.
internal sealed class PetCache : IPetCache
{
    private const string WikiPetSuffix = " (pet)";

    private sealed record Snapshot(HashSet<int> Ids, HashSet<string> LowerNames);

    private volatile Snapshot _snapshot = new([], []);

    public IReadOnlyCollection<int> ItemIds => _snapshot.Ids;
    public IReadOnlyCollection<string> LowerNames => _snapshot.LowerNames;

    public bool IsPet(int itemId, string? itemName = null)
    {
        var snapshot = _snapshot;
        if (snapshot.Ids.Contains(itemId)) return true;
        return !string.IsNullOrEmpty(itemName) && snapshot.LowerNames.Contains(itemName.Trim().ToLowerInvariant());
    }

    public void Replace(IEnumerable<CollectionLogEntryRef> pets)
    {
        var ids = new HashSet<int>();
        var names = new HashSet<string>();
        foreach (var pet in pets)
        {
            ids.Add(pet.ItemId);
            if (string.IsNullOrWhiteSpace(pet.Name)) continue;

            var lower = pet.Name.Trim().ToLowerInvariant();
            names.Add(lower);
            // The wiki disambiguates a pet whose name collides with another page ("Gull (pet)"); the
            // game names the item without the suffix.
            if (lower.EndsWith(WikiPetSuffix, StringComparison.Ordinal))
                names.Add(lower[..^WikiPetSuffix.Length]);
        }
        _snapshot = new Snapshot(ids, names);
    }
}
