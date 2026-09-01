using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// In-memory fan-out for the live roll ticker. Singleton; see ILootRollFeed for why this is not part
/// of LootFeedService.
/// </summary>
/// <remarks>
/// The concurrency model is deliberately the same as LootFeedService's, because that one is proven
/// and the failure modes are already understood:
///   - Partitions are keyed by scope and PRE-POPULATED at construction, so no key is ever added or
///     removed at runtime. That is what makes a plain Dictionary safe for the buffers - only the
///     values mutate, always under the partition's lock.
///   - One BOUNDED channel per subscriber with DropOldest, so a browser that stops reading loses
///     old rolls instead of blocking the publisher. On a ticker that is the right loss: nobody
///     scrolls back through it, and the newest rolls are the point.
///   - Publish never awaits. It takes the lock only to append to the ring, then fans out with
///     TryWrite outside it.
/// </remarks>
internal sealed class LootRollFeedService : ILootRollFeed
{
    private const int BufferCapacity = ILootRollFeed.BacklogSize;

    /// <summary>
    /// Per subscriber. Small on purpose: a client this far behind on a ticker wants the newest
    /// rolls, not a queue of stale ones, and DropOldest gives exactly that.
    /// </summary>
    private const int ChannelCapacity = 16;

    private readonly Dictionary<LootFeedScope, Queue<LootRollEntry>> _buffers;
    private readonly Dictionary<LootFeedScope, object> _locks;
    private readonly ConcurrentDictionary<LootFeedScope, ConcurrentDictionary<Guid, Channel<LootRollEntry>>> _subscribers;

    public LootRollFeedService()
    {
        var scopes = Enum.GetValues<LootFeedScope>();
        _buffers = scopes.ToDictionary(s => s, _ => new Queue<LootRollEntry>(BufferCapacity));
        _locks = scopes.ToDictionary(s => s, _ => new object());
        _subscribers = new ConcurrentDictionary<LootFeedScope, ConcurrentDictionary<Guid, Channel<LootRollEntry>>>(
            scopes.ToDictionary(s => s, _ => new ConcurrentDictionary<Guid, Channel<LootRollEntry>>()));
    }

    public IReadOnlyList<LootRollEntry> GetRecent(LootFeedScope scope)
    {
        lock (_locks[scope])
        {
            // Newest first, matching the order the banner renders in.
            var snapshot = _buffers[scope].ToArray();
            Array.Reverse(snapshot);
            return snapshot;
        }
    }

    public async IAsyncEnumerable<LootRollEntry> SubscribeAsync(
        LootFeedScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LootRollEntry>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers[scope].TryAdd(id, channel);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
                yield return entry;
        }
        finally
        {
            // The only unsubscribe path: the endpoint's await foreach unwinds when the request is
            // aborted, disposing the enumerator and running this.
            _subscribers[scope].TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    public void SeedBuffer(LootFeedScope scope, IEnumerable<LootRollEntry> entries)
    {
        lock (_locks[scope])
        {
            _buffers[scope].Clear();
            foreach (var entry in entries)
            {
                _buffers[scope].Enqueue(entry);
                while (_buffers[scope].Count > BufferCapacity)
                    _buffers[scope].Dequeue();
            }
        }

        // Deliberately does NOT notify subscribers. Seeding happens at startup, before anything is
        // connected; a subscriber that somehow existed would receive the whole buffer as if 40
        // kills had just landed, and the banner would animate them all in at once.
    }

    public void Publish(LootFeedScope scope, LootRollEntry entry)
    {
        lock (_locks[scope])
        {
            _buffers[scope].Enqueue(entry);
            while (_buffers[scope].Count > BufferCapacity)
                _buffers[scope].Dequeue();
        }

        // Outside the lock, and TryWrite rather than WriteAsync: a subscriber whose channel is full
        // silently drops its oldest roll instead of holding up an ingest batch.
        foreach (var (_, channel) in _subscribers[scope])
            channel.Writer.TryWrite(entry);
    }
}
