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

    // GroupKey -> nodes in _buffers[tier]. Usually 0–2 nodes per key (one open session, maybe a
    // stale older session waiting to roll off). Kept in sync with _buffers under _bufferLocks.
    private readonly Dictionary<LootFeedTier, Dictionary<string, List<LinkedListNode<LootFeedEntry>>>> _buffersByKey =
        ILootFeedService.AllTiers.ToDictionary(t => t, _ => new Dictionary<string, List<LinkedListNode<LootFeedEntry>>>());

    private readonly Dictionary<LootFeedTier, object> _bufferLocks =
        ILootFeedService.AllTiers.ToDictionary(t => t, _ => new object());

    private readonly ConcurrentDictionary<LootFeedTier, ConcurrentDictionary<Guid, Channel<LootFeedBroadcast>>> _subscribers = new(
        ILootFeedService.AllTiers.Select(t => new KeyValuePair<LootFeedTier, ConcurrentDictionary<Guid, Channel<LootFeedBroadcast>>>(t, new())));

    public IReadOnlyList<LootFeedEntry> GetCurrentEntries(LootFeedTier tier)
    {
        lock (_bufferLocks[tier])
        {
            return _buffers[tier].Reverse().ToArray();
        }
    }

    public async IAsyncEnumerable<LootFeedBroadcast> SubscribeAsync(
        LootFeedTier tier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LootFeedBroadcast>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers[tier].TryAdd(id, channel);

        try
        {
            await foreach (var broadcast in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return broadcast;
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
            var tier = entry.Tier;
            lock (_bufferLocks[tier])
            {
                var node = _buffers[tier].AddFirst(entry);
                AddToIndex(tier, entry.GroupKey, node);
                while (_buffers[tier].Count > BufferCapacity)
                {
                    EvictFirst(tier);
                }
            }
        }
    }

    public void Publish(LootFeedEntry entry)
    {
        var tier = entry.Tier;
        LootFeedBroadcast broadcast;

        lock (_bufferLocks[tier])
        {
            var matchedNode = FindBestMatch(tier, entry);
            if (matchedNode is not null)
            {
                var previous = matchedNode.Value;
                var merged = LootFeedGrouping.Merge(previous, entry);

                // Bubble the merged group to the tail of the buffer so it represents the newest activity.
                _buffers[tier].Remove(matchedNode);
                RemoveFromIndex(tier, previous.GroupKey, matchedNode);

                var newNode = _buffers[tier].AddLast(merged);
                AddToIndex(tier, merged.GroupKey, newNode);

                broadcast = new LootFeedBroadcast(merged, previous.DomId);
            }
            else
            {
                var newNode = _buffers[tier].AddLast(entry);
                AddToIndex(tier, entry.GroupKey, newNode);
                while (_buffers[tier].Count > BufferCapacity)
                {
                    EvictFirst(tier);
                }
                broadcast = new LootFeedBroadcast(entry, null);
            }
        }

        foreach (var (_, channel) in _subscribers[tier])
        {
            channel.Writer.TryWrite(broadcast);
        }
    }

    private LinkedListNode<LootFeedEntry>? FindBestMatch(LootFeedTier tier, LootFeedEntry entry)
    {
        if (!_buffersByKey[tier].TryGetValue(entry.GroupKey, out var candidates) || candidates.Count == 0)
            return null;

        LinkedListNode<LootFeedEntry>? best = null;
        var bestDelta = TimeSpan.MaxValue;
        foreach (var node in candidates)
        {
            var delta = LootFeedGrouping.TryGetMergeDelta(node.Value, entry);
            if (delta is null) continue;
            if (delta.Value < bestDelta)
            {
                bestDelta = delta.Value;
                best = node;
            }
        }
        return best;
    }

    private void AddToIndex(LootFeedTier tier, string groupKey, LinkedListNode<LootFeedEntry> node)
    {
        var index = _buffersByKey[tier];
        if (!index.TryGetValue(groupKey, out var list))
        {
            list = [];
            index[groupKey] = list;
        }
        list.Add(node);
    }

    private void RemoveFromIndex(LootFeedTier tier, string groupKey, LinkedListNode<LootFeedEntry> node)
    {
        var index = _buffersByKey[tier];
        if (!index.TryGetValue(groupKey, out var list)) return;
        list.Remove(node);
        if (list.Count == 0)
            index.Remove(groupKey);
    }

    private void EvictFirst(LootFeedTier tier)
    {
        var first = _buffers[tier].First;
        if (first is null) return;
        var groupKey = first.Value.GroupKey;
        _buffers[tier].RemoveFirst();
        RemoveFromIndex(tier, groupKey, first);
    }
}
