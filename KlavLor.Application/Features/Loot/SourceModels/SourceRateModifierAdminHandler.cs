using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Features.Loot.SourceModels;

// Backs the admin "source rate modifiers" panel: search sources, set/clear the multiplier that
// scales a source's (or a single item's) expected kills-to-drop. Every write reprimes the
// singleton cache so it takes effect immediately on the character page and on the next
// leaderboard rebuild, which it now requests rather than waiting an hour for.
public sealed class SourceRateModifierAdminHandler(
    ISourceRateModifierRepository repository,
    ISourceRateModifierCache cache,
    RecomputeTrigger recompute)
{
    public const int SearchLimit = 40;

    public Task<List<SourceRateModifierRow>> Search(string? term) => repository.Search(term, SearchLimit);

    public async Task<List<SourceRateModifierRow>> Apply(string sourceName, string? itemName, double multiplier)
    {
        var source = (sourceName ?? "").Trim();
        var item = (itemName ?? "").Trim();
        if (source.Length == 0) return await repository.Search(null, SearchLimit);

        // A multiplier of exactly 1 is a no-op — treat "set to 1" as "remove" so the list stays
        // to just the meaningful overrides.
        if (Math.Abs(multiplier - 1.0) < 1e-9)
            await repository.Delete(source, item);
        else
            await repository.Upsert(source, item, multiplier);

        await Reprime();
        return await repository.Search(null, SearchLimit);
    }

    public async Task<List<SourceRateModifierRow>> Remove(string sourceName, string itemName)
    {
        await repository.Delete(sourceName, itemName ?? "");
        await Reprime();
        return await repository.Search(null, SearchLimit);
    }

    // Reprime the cache (character pages read it live) and request a board rebuild, which is the
    // only consumer that can't pick the change up on its own next request.
    private async Task Reprime()
    {
        cache.Replace(await repository.GetAll());
        await recompute.LuckInputsChanged();
    }
}
