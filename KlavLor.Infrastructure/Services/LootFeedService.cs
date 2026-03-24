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

    private readonly ConcurrentDictionary<LootFeedTier, ConcurrentQueue<LootFeedEntry>> _buffers = new(
        ILootFeedService.AllTiers.Select(t => new KeyValuePair<LootFeedTier, ConcurrentQueue<LootFeedEntry>>(t, new())));

    private readonly ConcurrentDictionary<LootFeedTier, ConcurrentDictionary<Guid, Channel<LootFeedEntry>>> _subscribers = new(
        ILootFeedService.AllTiers.Select(t => new KeyValuePair<LootFeedTier, ConcurrentDictionary<Guid, Channel<LootFeedEntry>>>(t, new())));

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
            _buffers[entry.Tier].Enqueue(entry);
        }
    }

    public void Publish(LootFeedEntry entry)
    {
        var buffer = _buffers[entry.Tier];

        buffer.Enqueue(entry);

        while (buffer.Count > BufferCapacity && buffer.TryDequeue(out _))
        {
        }

        foreach (var (_, channel) in _subscribers[entry.Tier])
        {
            channel.Writer.TryWrite(entry);
        }
    }
}
