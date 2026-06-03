using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Maintenance;

// Admin rename/merge of loot-source names. Bulk-mutates loot, so every rename is logged
// with the acting admin and busts the global source-page cache.
public sealed class SourceAdminHandler(
    ISourceAdminRepository repository,
    IMemoryCache cache,
    ICurrentUser currentUser,
    ILogger<SourceAdminHandler> logger)
{
    public const int SearchLimit = 50;

    public Task<List<SourceNameRow>> Search(string? term) => repository.Search(term, SearchLimit);

    public async Task<SourceRenameResult> Rename(string from, string to)
    {
        from = from.Trim();
        to = to.Trim();

        // No-op on empty or unchanged targets.
        if (from.Length == 0 || to.Length == 0 || string.Equals(from, to, StringComparison.Ordinal))
            return new SourceRenameResult(from, to, 0);

        var moved = await repository.RenameSource(from, to);

        // Global source-page aggregates changed for both the old and new names.
        GlobalSourceCache.Invalidate(cache);

        logger.LogWarning("Admin {UserId} renamed loot source {From} -> {To} ({Moved} records moved)",
            currentUser.UserId, from, to, moved);

        return new SourceRenameResult(from, to, moved);
    }
}
