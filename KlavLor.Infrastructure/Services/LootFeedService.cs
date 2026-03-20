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

    private readonly ConcurrentQueue<LootFeedEntry> _buffer = new();
    private readonly ConcurrentDictionary<Guid, Channel<LootFeedEntry>> _subscribers = new();
    private int _bufferCount;

    public IReadOnlyList<LootFeedEntry> GetCurrentEntries()
    {
        return _buffer.OrderByDescending(e => e.OccurredAt).ToArray();
    }

    public async IAsyncEnumerable<LootFeedEntry> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LootFeedEntry>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers.TryAdd(id, channel);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    public void Publish(LootFeedEntry entry)
    {
        _buffer.Enqueue(entry);
        var count = Interlocked.Increment(ref _bufferCount);

        while (count > BufferCapacity && _buffer.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _bufferCount);
        }

        foreach (var (_, channel) in _subscribers)
        {
            channel.Writer.TryWrite(entry);
        }
    }
}
