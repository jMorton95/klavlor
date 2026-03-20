using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.Application.Interfaces.Services;

public interface ILootFeedService
{
    IReadOnlyList<LootFeedEntry> GetCurrentEntries();
    IAsyncEnumerable<LootFeedEntry> SubscribeAsync(CancellationToken cancellationToken);
    void Publish(LootFeedEntry entry);
}
