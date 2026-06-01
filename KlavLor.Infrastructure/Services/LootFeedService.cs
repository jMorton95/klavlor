using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

internal sealed class LootFeedService(ILootFeedHighlightTracker highlights) : ILootFeedService
{
    private const int BufferCapacity = 50;
    private const int ChannelCapacity = 10;

    // Buffers, indexes, locks, and subscribers are keyed by (scope, tier). Each pair gets
    // its own independent buffer + subscriber set so the main and leagues feeds don't
    // contend on shared state.
    private static readonly (LootFeedScope Scope, LootFeedTier Tier)[] AllPartitions =
        Enum.GetValues<LootFeedScope>()
            .SelectMany(s => ILootFeedService.AllTiers.Select(t => (s, t)))
            .ToArray();

    private readonly Dictionary<(LootFeedScope, LootFeedTier), LinkedList<LootFeedEntry>> _buffers =
        AllPartitions.ToDictionary(p => (p.Scope, p.Tier), _ => new LinkedList<LootFeedEntry>());

    // GroupKey -> nodes in _buffers[scope, tier]. Kept in sync with _buffers under _bufferLocks.
    private readonly Dictionary<(LootFeedScope, LootFeedTier), Dictionary<string, List<LinkedListNode<LootFeedEntry>>>> _buffersByKey =
        AllPartitions.ToDictionary(p => (p.Scope, p.Tier), _ => new Dictionary<string, List<LinkedListNode<LootFeedEntry>>>());

    private readonly Dictionary<(LootFeedScope, LootFeedTier), object> _bufferLocks =
        AllPartitions.ToDictionary(p => (p.Scope, p.Tier), _ => new object());

    private readonly ConcurrentDictionary<(LootFeedScope, LootFeedTier), ConcurrentDictionary<Guid, Channel<LootFeedBroadcast>>> _subscribers = new(
        AllPartitions.Select(p => new KeyValuePair<(LootFeedScope, LootFeedTier), ConcurrentDictionary<Guid, Channel<LootFeedBroadcast>>>((p.Scope, p.Tier), new())));

    public IReadOnlyList<LootFeedEntry> GetCurrentEntries(LootFeedScope scope, LootFeedTier tier)
    {
        var key = (scope, tier);
        lock (_bufferLocks[key])
        {
            return _buffers[key].Reverse().ToArray();
        }
    }

    public async IAsyncEnumerable<LootFeedBroadcast> SubscribeAsync(
        LootFeedScope scope,
        LootFeedTier tier,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var key = (scope, tier);
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<LootFeedBroadcast>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers[key].TryAdd(id, channel);

        try
        {
            await foreach (var broadcast in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return broadcast;
            }
        }
        finally
        {
            _subscribers[key].TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    public void SeedBuffer(LootFeedScope scope, IEnumerable<LootFeedEntry> entries)
    {
        // Input is ordered newest-first; buffer convention is First=oldest, Last=newest.
        var touchedTiers = new HashSet<LootFeedTier>();
        foreach (var entry in entries)
        {
            var key = (scope, entry.Tier);
            lock (_bufferLocks[key])
            {
                var node = _buffers[key].AddFirst(entry);
                AddToIndex(key, entry.GroupKey, node);
                while (_buffers[key].Count > BufferCapacity)
                {
                    EvictFirst(key);
                }
            }
            touchedTiers.Add(entry.Tier);
        }

        // Prime the highlight tracker once the buffer is fully populated, so the
        // crown reflects every seeded entry (not just whichever tier landed last).
        foreach (var tier in touchedTiers)
        {
            var key = (scope, tier);
            LootFeedEntry[] snapshot;
            lock (_bufferLocks[key])
            {
                snapshot = _buffers[key].ToArray();
            }
            highlights.SetInitial(scope, tier, snapshot);
        }
    }

    public void Publish(LootFeedEntry entry)
    {
        var key = (entry.Scope, entry.Tier);
        LootFeedBroadcast broadcast;

        lock (_bufferLocks[key])
        {
            var matchedNode = FindBestMatch(key, entry);
            HighlightChange? highlightChange;
            if (matchedNode is not null)
            {
                var previous = matchedNode.Value;
                var merged = LootFeedGrouping.Merge(previous, entry);

                // Bubble the merged group to the tail of the buffer so it represents the newest activity.
                _buffers[key].Remove(matchedNode);
                RemoveFromIndex(key, previous.GroupKey, matchedNode);

                var newNode = _buffers[key].AddLast(merged);
                AddToIndex(key, merged.GroupKey, newNode);

                highlightChange = highlights.OnBufferChanged(entry.Scope, entry.Tier, _buffers[key]);
                broadcast = new LootFeedBroadcast(merged, previous.DomId, highlightChange);
            }
            else
            {
                var newNode = _buffers[key].AddLast(entry);
                AddToIndex(key, entry.GroupKey, newNode);
                while (_buffers[key].Count > BufferCapacity)
                {
                    EvictFirst(key);
                }
                highlightChange = highlights.OnBufferChanged(entry.Scope, entry.Tier, _buffers[key]);
                broadcast = new LootFeedBroadcast(entry, null, highlightChange);
            }
        }

        foreach (var (_, channel) in _subscribers[key])
        {
            channel.Writer.TryWrite(broadcast);
        }
    }

    private LinkedListNode<LootFeedEntry>? FindBestMatch((LootFeedScope, LootFeedTier) key, LootFeedEntry entry)
    {
        if (!_buffersByKey[key].TryGetValue(entry.GroupKey, out var candidates) || candidates.Count == 0)
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

    private void AddToIndex((LootFeedScope, LootFeedTier) key, string groupKey, LinkedListNode<LootFeedEntry> node)
    {
        var index = _buffersByKey[key];
        if (!index.TryGetValue(groupKey, out var list))
        {
            list = [];
            index[groupKey] = list;
        }
        list.Add(node);
    }

    private void RemoveFromIndex((LootFeedScope, LootFeedTier) key, string groupKey, LinkedListNode<LootFeedEntry> node)
    {
        var index = _buffersByKey[key];
        if (!index.TryGetValue(groupKey, out var list)) return;
        list.Remove(node);
        if (list.Count == 0)
            index.Remove(groupKey);
    }

    private void EvictFirst((LootFeedScope, LootFeedTier) key)
    {
        var first = _buffers[key].First;
        if (first is null) return;
        var groupKey = first.Value.GroupKey;
        _buffers[key].RemoveFirst();
        RemoveFromIndex(key, groupKey, first);
    }
}
