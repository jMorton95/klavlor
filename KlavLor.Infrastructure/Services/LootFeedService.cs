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

    private readonly Dictionary<LootFeedTier, LinkedList<LootFeedEntry>> _buffers =
        ILootFeedService.AllTiers.ToDictionary(t => t, _ => new LinkedList<LootFeedEntry>());

    private readonly Dictionary<LootFeedTier, object> _bufferLocks =
        ILootFeedService.AllTiers.ToDictionary(t => t, _ => new object());

    private readonly ConcurrentDictionary<LootFeedTier, ConcurrentDictionary<Guid, Channel<LootFeedEntry>>> _subscribers = new(
        ILootFeedService.AllTiers.Select(t => new KeyValuePair<LootFeedTier, ConcurrentDictionary<Guid, Channel<LootFeedEntry>>>(t, new())));

    public IReadOnlyList<LootFeedEntry> GetCurrentEntries(LootFeedTier tier)
    {
        lock (_bufferLocks[tier])
        {
            return _buffers[tier].Reverse().ToArray();
        }
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
        // Input is ordered newest-first; buffer convention is First=oldest, Last=newest.
        foreach (var entry in entries)
        {
            var buffer = _buffers[entry.Tier];
            lock (_bufferLocks[entry.Tier])
            {
                buffer.AddFirst(entry);
                while (buffer.Count > BufferCapacity)
                {
                    buffer.RemoveFirst();
                }
            }
        }
    }

    public void Publish(LootFeedEntry entry)
    {
        var buffer = _buffers[entry.Tier];
        LootFeedEntry broadcastEntry;

        lock (_bufferLocks[entry.Tier])
        {
            if (buffer.Last is { } tail && LootFeedGrouping.CanMerge(tail.Value, entry))
            {
                var merged = LootFeedGrouping.Merge(tail.Value, entry);
                tail.Value = merged;
                broadcastEntry = merged;
            }
            else
            {
                buffer.AddLast(entry);
                while (buffer.Count > BufferCapacity)
                {
                    buffer.RemoveFirst();
                }
                broadcastEntry = entry;
            }
        }

        foreach (var (_, channel) in _subscribers[entry.Tier])
        {
            channel.Writer.TryWrite(broadcastEntry);
        }
    }
}
