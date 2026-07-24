namespace KlavLor.Domain.Entities;

// IsSpecial marks an admin-injected untradeable "giga" drop (Infernal Cape, Dizana's Quiver):
// zero value, forced to the top feed tier, and rendered with the distinct feed effect.
public sealed record LootDrop(string Name, int ItemId, int Quantity, int Price, bool IsFirstTime = false, bool IsSpecial = false);
