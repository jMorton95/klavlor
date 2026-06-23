using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Features.Loot.Feed;

public sealed class LootFeedTiersHandler(ILootLogRepository lootLogRepository)
{
    public const int EntriesPerTier = 50;

    public Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> Handle(
        LootFeedScope scope = LootFeedScope.Main,
        IReadOnlySet<LootFeedTier>? requestedTiers = null)
        => lootLogRepository.GetAllFeedTiers(EntriesPerTier, scope, requestedTiers);
}
