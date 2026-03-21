using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

internal sealed class LootFeedService : ILootFeedService
{
    private const int BufferCapacity = 50;
    private const int ChannelCapacity = 10;

    private static readonly LootFeedTier[] AllTiers = [LootFeedTier.Standard, LootFeedTier.Notable, LootFeedTier.Mega];

    private readonly ConcurrentDictionary<LootFeedTier, ConcurrentQueue<LootFeedEntry>> _buffers = new(
        AllTiers.Select(t => new KeyValuePair<LootFeedTier, ConcurrentQueue<LootFeedEntry>>(t, new())));

    private readonly ConcurrentDictionary<LootFeedTier, ConcurrentDictionary<Guid, Channel<LootFeedEntry>>> _subscribers = new(
        AllTiers.Select(t => new KeyValuePair<LootFeedTier, ConcurrentDictionary<Guid, Channel<LootFeedEntry>>>(t, new())));

    public IReadOnlyList<LootFeedEntry> GetCurrentEntries(LootFeedTier tier)
    {
        return _buffers[tier].OrderByDescending(e => e.OccurredAt).ToArray();
    }

    public async IAsyncEnumerable<LootFeedEntry> SubscribeAsync(
        LootFeedTier tier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LootFeedEntry>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers[tier].TryAdd(id, channel);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }
        finally
        {
            _subscribers[tier].TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    public void SeedBuffer(IEnumerable<LootFeedEntry> entries)
    {
        foreach (var entry in entries)
        {
            var tier = ILootFeedService.GetTier(entry.TotalValue);
            _buffers[tier].Enqueue(entry);
        }
    }

    public void Publish(LootFeedEntry entry)
    {
        var tier = ILootFeedService.GetTier(entry.TotalValue);
        var buffer = _buffers[tier];

        buffer.Enqueue(entry);

        while (buffer.Count > BufferCapacity && buffer.TryDequeue(out _))
        {
        }

        foreach (var (_, channel) in _subscribers[tier])
        {
            channel.Writer.TryWrite(entry);
        }
    }
}
