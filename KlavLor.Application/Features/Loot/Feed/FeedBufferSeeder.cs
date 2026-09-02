using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Features.Loot.Feed;

/// <summary>
/// Rebuilds the live feed's in-memory swimlane buffers from the database, for every scope.
/// </summary>
/// <remarks>
/// TWO CALLERS, AND THAT IS THE POINT.
///
/// <para>
/// <c>LootFeedSeederService</c> runs it at startup, because the buffers are in memory and the lanes
/// would otherwise be blank until the clan's next drop.
/// </para>
///
/// <para>
/// The item-value admin runs it on every write, because an override changes what a stored drop is
/// WORTH, and the buffer is full of entries that were priced before the write. That was a real bug:
/// an untradeable is reported by RuneLite at 0 GP, so it sits under the feed's 10k floor and never
/// enters a lane at all. Setting an intrinsic value re-priced the database — so a page load, which
/// reads the database, showed the drop correctly — while the buffer went on holding a card that had
/// never heard of it. The next live kill at that source merged against the stale buffer entry and
/// broadcast a card missing every earlier receipt. It LOOKED like the card refused to update: the
/// roll count and KC range still climbed, because those are ordinals resolved fresh on each publish,
/// while the chips and the total stood still. A restart "fixed" it, which is exactly what a stale
/// in-memory buffer looks like from the outside.
/// </para>
///
/// <para>
/// This is a database read across every tier and scope — the same work startup already does, and an
/// override write is a rare admin edit, so it runs inline rather than through a job trigger. Note it
/// deliberately does NOT notify subscribers: <c>SeedBuffer</c> replaces rather than appends, and
/// anyone watching would otherwise see the whole backlog animate in as if it had just landed.
/// </para>
/// </remarks>
public sealed class FeedBufferSeeder(LootFeedTiersHandler tiers, ILootFeedService feed)
{
    /// <summary>Reseeds every scope. Returns how many entries were loaded, for the job log.</summary>
    /// <remarks>
    /// Goes through the handler rather than straight to the repository, so seeded entries carry the
    /// per-drop effective rates. Buffered entries are merged with live drops on publish, and a
    /// merged card would otherwise show rate chips only on the freshly arrived items.
    /// </remarks>
    public async Task<int> Reseed()
    {
        var seeded = 0;

        // Each scope independently, so main and leagues both have history immediately.
        foreach (var scope in Enum.GetValues<LootFeedScope>())
        {
            var byTier = await tiers.Handle(scope);

            foreach (var (_, entries) in byTier)
            {
                // An empty tier leaves its buffer alone rather than clearing it. The read is capped
                // per tier, so "nothing came back" is not the same claim as "there is nothing".
                if (entries.Count == 0) continue;

                feed.SeedBuffer(scope, entries);
                seeded += entries.Count;
            }
        }

        return seeded;
    }
}
