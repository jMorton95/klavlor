using KlavLor.Application.Features.CollectionLog;

namespace KlavLor.Application.Interfaces.Repositories;

/// <summary>
/// Read side of the Temple-sourced collection log.
/// </summary>
/// <remarks>
/// Every query here reads only the collection-log tables and the character roster. It never touches
/// LootRecords or drop rates, with one deliberate exception: the per-item comparison, which asks our
/// own data for a kill count so a holder can be shown in context. That split is the whole
/// reconciliation rule — Temple owns what a character HAS, our loot data owns how many rolls it
/// took — and keeping it at the query boundary is what stops the two blending by accident.
/// </remarks>
public interface ICollectionLogQueryRepository
{
    /// <summary>The clan board: one row per character, ranked. Reads the state table only.</summary>
    Task<List<CollectionLogStanding>> GetStandings();

    /// <summary>One character's header and per-category progress. Null when the character is unknown.</summary>
    Task<CharacterCollectionLog?> GetCharacterLog(int gameCharacterId);

    /// <summary>The items of one category for one character, obtained and missing alike.</summary>
    Task<List<CollectionLogItemState>> GetCategoryItems(int gameCharacterId, string categorySlug);

    /// <summary>One category across every character with data.</summary>
    Task<CollectionLogCategoryComparison?> GetCategoryComparison(string categorySlug);

    /// <summary>One item across every character, with our own kill count where we have one.</summary>
    Task<CollectionLogItemComparison?> GetItemComparison(int itemId);

    /// <summary>Item search across the whole log. Blank term returns the rarest-held items.</summary>
    Task<List<CollectionLogSearchRow>> SearchItems(string? term, int limit);
}
