using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace KlavLor.Application.Features.Loot.Feed;

/// <summary>
/// Hover-popover data for a (character, source) pair on the loot feed. Backed by
/// <see cref="LootStatsCache"/> so the hot path on repeated hovers is an in-memory
/// dictionary read; the cache is invalidated per-character by the loot-ingest path,
/// which means a fresh kill at any source bumps the version and forces a refetch
/// the next time anyone hovers that character's cards.
/// </summary>
public sealed class SourcePopoverHandler(
    ILootSourceDetailRepository sourceDetailRepository,
    IMemoryCache memoryCache)
{
    public async Task<Result<SourcePopoverData>> Handle(int characterId, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return Result<SourcePopoverData>.Failure("Source name is required.");

        var version = LootStatsCache.GetVersion(memoryCache, characterId);
        var key = LootStatsCache.EntryKey(characterId, version, "SourcePopover", sourceName);

        if (memoryCache.TryGetValue(key, out SourcePopoverData? cached) && cached is not null)
            return Result<SourcePopoverData>.Success(cached);

        var data = await sourceDetailRepository.GetSourcePopover(characterId, sourceName);
        memoryCache.Set(key, data, LootStatsCache.EntryTtl);
        return Result<SourcePopoverData>.Success(data);
    }
}
