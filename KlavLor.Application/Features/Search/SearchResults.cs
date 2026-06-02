using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Search;

// Lightweight, capped projections backing each section of the database-search page.
// Each section is fetched by its own HTTP request (separate DI scope), so these never
// travel together — they exist only to keep the section Razor components strongly typed.

public sealed record SearchCharacterResult(
    int GameCharacterId,
    string CharacterName,
    string UserName,
    int TotalSources,
    long TotalKills,
    long TotalValue);

public sealed record SearchSourceResult(
    string SourceName,
    LootSourceType SourceType,
    long TotalKills,
    long TotalValue);

// A loot item as it actually dropped (from LootRecord.DropsJson), aggregated across
// every source it has come from. Links to the global source page of its top origin.
public sealed record SearchDropResult(
    string ItemName,
    long TotalQuantity,
    long TotalValue,
    int SourceCount,
    string TopSourceName);

// Reference/catalog items: GearItem (template builder) and CollectionLogItem (wiki).
public sealed record SearchItemResult(
    string Name,
    SearchItemKind Kind,
    string? WikiUrl,
    string? ImageUrl);

public enum SearchItemKind
{
    GearItem,
    CollectionLogItem
}
