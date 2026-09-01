using System.Collections.Concurrent;

namespace KlavLor.Web.Application.Features.Loot.Feed;

/// <summary>
/// Assigns each character a colour on the roll ticker, so a glance at the banner separates who is
/// killing what without reading a single name.
/// </summary>
/// <remarks>
/// ASSIGNMENT ORDER, NOT A HASH OF THE NAME. A hash needs no memory and would survive a restart,
/// but it can collide, and on a clan this size a collision is not a rare edge case - two of five
/// characters sharing a colour defeats the whole point. First come, first served across a palette
/// of twelve guarantees every character is distinct until the thirteenth, which we will not reach.
/// It follows the profile charts' StackPalette in this: rank-ordered index into a fixed list,
/// wrapping with modulo rather than falling back to grey.
///
/// The map is LIVE MEMORY - a static dictionary for the life of the process, deliberately matching
/// the lifetime of the ticker's own ring buffer, which is the only thing that can still be showing
/// an older chip. A restart reshuffles both together, so nothing on screen ever disagrees with it.
/// Static rather than injected for the same reason LootFeedEndpoint's rendered-chip cache is: it is
/// presentation memory belonging to one endpoint, with no configuration and nothing to dispose.
///
/// The classes are OURS (app.css), not Tailwind utilities, because these names are computed and
/// Tailwind's scanner only emits classes it can see spelled out in source. app.css carries the
/// light/dark pair for each hue - 700 on the near-white band, 400 on the dark one - so the caller
/// never has to know which theme it is rendering for.
/// </remarks>
internal static class RollChipHues
{
    /// <summary>Must match the number of .roll-hue-N rules in app.css.</summary>
    public const int Count = 12;

    private static readonly ConcurrentDictionary<string, int> Assigned =
        new(StringComparer.OrdinalIgnoreCase);

    private static int _next = -1;

    /// <summary>
    /// The hue class for a character. Case-insensitive, so a display name that changes case keeps
    /// its colour rather than quietly claiming a second one.
    /// </summary>
    public static string ClassFor(string? characterName)
    {
        var key = characterName ?? string.Empty;
        var index = Assigned.GetOrAdd(key, _ => Interlocked.Increment(ref _next) % Count);
        return $"roll-hue-{index + 1}";
    }
}
