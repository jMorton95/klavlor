using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.Application.Interfaces.Services;

/// <summary>
/// The live roll ticker: every kill as it lands, with no loot attached.
/// </summary>
/// <remarks>
/// SEPARATE FROM ILootFeedService ON PURPOSE. The two answer different questions and have different
/// shapes: the feed is partitioned by tier, merges kills into grouped cards, and only carries drops
/// worth 10k or more. The ticker has no tiers, no grouping, no value floor and no loot - so folding
/// it into the feed service would mean a tier dimension that is always meaningless and a merge path
/// that must always be skipped.
///
/// Three things make it much cheaper than the feed, which is the whole reason it can afford to
/// carry every kill:
///   - ONE stream per scope, not one per tier.
///   - The connect backfill is the in-memory ring buffer, so opening the page costs no query. The
///     feed's backfill is a database read per lane.
///   - A roll is four small fields, and its chip is byte-identical for every viewer (no filter, no
///     merge, no highlight), so the endpoint renders it ONCE and memoises it. The feed renders a
///     Razor component per event PER SUBSCRIBER, which it can afford only because it publishes so
///     much less.
/// </remarks>
public interface ILootRollFeed
{
    /// <summary>
    /// How many rolls the ring holds, and therefore how many a connecting page is replayed. Enough
    /// to fill the banner and no more - this is the backfill, not a history, and the ticker has no
    /// scrollback.
    ///
    /// The banner's DOM cap is this same number (data-max-chips on the track), which is why it is
    /// public. When the two disagreed - a ring of 40 against a cap of 30 - every connect built ten
    /// chips, animated them and immediately destroyed them.
    /// </summary>
    public const int BacklogSize = 40;

    /// <summary>The recent rolls held in memory, newest first - the connect backfill.</summary>
    IReadOnlyList<LootRollEntry> GetRecent(LootFeedScope scope);

    /// <summary>Streams rolls as they are published. Completes when the caller cancels.</summary>
    IAsyncEnumerable<LootRollEntry> SubscribeAsync(LootFeedScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one kill. Synchronous, non-blocking and never throws: it sits on the ingest hot
    /// path, and a stalled browser must not be able to slow down a sync.
    /// </summary>
    void Publish(LootFeedScope scope, LootRollEntry entry);

    /// <summary>
    /// Fills the buffer at startup, oldest first, so the banner is populated on the first page load
    /// after a restart rather than blank until the next kill. Replaces whatever is there.
    /// </summary>
    void SeedBuffer(LootFeedScope scope, IEnumerable<LootRollEntry> entries);
}
