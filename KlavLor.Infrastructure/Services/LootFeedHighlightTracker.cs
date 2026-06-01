using System.Globalization;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// Singleton holding per-(scope, tier) "biggest drop" crowns. Updates only happen
/// inside the LootFeedService partition lock, so writes are serialised per key.
/// Reads use a <c>volatile</c> reference swap so the static render path is lock-free.
/// </summary>
internal sealed class LootFeedHighlightTracker : ILootFeedHighlightTracker
{
    // Std / Uncommon / Rare use a sliding-window cutoff. Epic / Legendary use a
    // calendar-month cutoff instead so the bigger swimlanes naturally reset each month.
    private static readonly TimeSpan RollingWindow = TimeSpan.FromDays(7);

    private readonly Dictionary<(LootFeedScope, LootFeedTier), object> _slotLocks =
        BuildAllKeys().ToDictionary(k => k, _ => new object());

    private readonly Dictionary<(LootFeedScope, LootFeedTier), Crown?> _slots =
        BuildAllKeys().ToDictionary(k => k, _ => (Crown?)null);

    public bool IsHighlight(LootFeedEntry entry)
    {
        // Lockless read — the slot ref is published via volatile write in UpdateSlot.
        var current = Volatile.Read(ref GetSlotRef(entry.Scope, entry.Tier));
        return current is not null && current.DomId == entry.DomId;
    }

    public string LabelFor(LootFeedTier tier, DateTimeOffset asOf)
    {
        if (IsMonthlyTier(tier))
        {
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(asOf.UtcDateTime.Month);
            return $"Biggest in {monthName}";
        }
        return "Biggest this week";
    }

    public void SetInitial(LootFeedScope scope, LootFeedTier tier, IEnumerable<LootFeedEntry> bufferSnapshot)
    {
        var winner = PickWinner(tier, bufferSnapshot, DateTimeOffset.UtcNow);
        UpdateSlot(scope, tier, winner);
    }

    public HighlightChange? OnBufferChanged(LootFeedScope scope, LootFeedTier tier, IEnumerable<LootFeedEntry> bufferSnapshot)
    {
        var now = DateTimeOffset.UtcNow;
        var candidate = PickWinner(tier, bufferSnapshot, now);

        lock (_slotLocks[(scope, tier)])
        {
            var current = _slots[(scope, tier)];

            // Three transitions matter: same crown (no-op), demote-only (window expired
            // and no replacement), promote-only / swap (new winner is stronger or the
            // crown was vacant). Strict greater-than means ties don't supersede.
            var currentValid = current is not null && IsInWindow(tier, current.OccurredAt, now);
            var candidateBeats = candidate is not null &&
                                 (!currentValid || candidate.TotalValue > current!.Value);

            if (!candidateBeats && currentValid) return null;
            if (candidate is null && current is null) return null;

            LootFeedEntry? demoted = null;
            if (current is not null && (candidate is null || candidate.DomId != current.DomId))
            {
                demoted = current.Entry;
            }

            LootFeedEntry? promoted = null;
            if (candidate is not null && (current is null || candidate.DomId != current.DomId))
            {
                promoted = candidate;
            }

            if (demoted is null && promoted is null) return null;

            UpdateSlot(scope, tier, candidate);
            return new HighlightChange(demoted, promoted);
        }
    }

    private static LootFeedEntry? PickWinner(LootFeedTier tier, IEnumerable<LootFeedEntry> entries, DateTimeOffset asOf) =>
        entries
            .Where(e => IsInWindow(tier, e.OccurredAt, asOf))
            .OrderByDescending(e => e.TotalValue)
            .ThenBy(e => e.OccurredAt) // tie → earlier card keeps the crown
            .FirstOrDefault();

    private static bool IsInWindow(LootFeedTier tier, DateTimeOffset occurredAt, DateTimeOffset asOf)
    {
        if (IsMonthlyTier(tier))
        {
            var monthStart = new DateTimeOffset(asOf.UtcDateTime.Year, asOf.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
            return occurredAt >= monthStart;
        }
        return occurredAt >= asOf - RollingWindow;
    }

    private static bool IsMonthlyTier(LootFeedTier tier) =>
        tier is LootFeedTier.Epic or LootFeedTier.Legendary;

    private void UpdateSlot(LootFeedScope scope, LootFeedTier tier, LootFeedEntry? winner)
    {
        Crown? next = winner is null
            ? null
            : new Crown(winner.DomId, winner.TotalValue, winner.OccurredAt, winner);
        Volatile.Write(ref GetSlotRef(scope, tier), next);
    }

    private ref Crown? GetSlotRef(LootFeedScope scope, LootFeedTier tier) =>
        ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_slots, (scope, tier));

    private static IEnumerable<(LootFeedScope, LootFeedTier)> BuildAllKeys() =>
        Enum.GetValues<LootFeedScope>()
            .SelectMany(s => ILootFeedService.AllTiers.Select(t => (s, t)));

    private sealed record Crown(string DomId, long Value, DateTimeOffset OccurredAt, LootFeedEntry Entry);
}
