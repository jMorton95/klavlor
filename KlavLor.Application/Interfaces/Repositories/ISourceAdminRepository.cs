using KlavLor.Application.Features.Maintenance;

namespace KlavLor.Application.Interfaces.Repositories;

// Admin curation of loot-source names: list distinct names and rename/merge variants.
public interface ISourceAdminRepository
{
    // Distinct source names with their loot counts. Blank term → busiest sources;
    // otherwise names matching the term.
    Task<List<SourceNameRow>> Search(string? term, int limit);

    // Repoints every LootRecord from one source name to another (a merge when the target
    // already exists), dropping the variant's derived drop-rate rows and source icon so
    // they're re-derived for the canonical name. Returns the number of records moved.
    Task<int> RenameSource(string from, string to);
}
