using Microsoft.Extensions.Logging;
using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Progression;

/// <summary>
/// Auto-completes gear-progression template nodes when the owning user receives the matching
/// item as a drop, stamping each completion with a flavourful note (source, kill count, date,
/// and how lucky/dry the drop was vs. the wiki rate). Invoked from the loot ingest pipeline on
/// first-time drops only, so the completion lands on the user's earliest receipt of the item.
///
/// Best-effort: any failure is swallowed and logged so a progression hiccup never blocks the
/// core loot ingest.
/// </summary>
public sealed class ProgressionAutoCompletionHandler(
    IUserNodeCompletionRepository completions,
    IDropRateRepository dropRates,
    ILootRecordRepository lootRecords,
    ILogger<ProgressionAutoCompletionHandler> logger)
{
    /// <summary>Context for one first-time item receipt that could complete a template node.</summary>
    public sealed record FirstUnlock(
        string ItemName,
        string SourceName,
        DateTimeOffset OccurredAt,
        int? RealKillCount,
        int CharacterId,
        int RecordId);

    public async Task Run(int userId, IReadOnlyList<FirstUnlock> unlocks)
    {
        if (unlocks.Count == 0)
            return;

        try
        {
            // One receipt per item — the user's earliest, since that's the real "unlock" moment
            // and a node only completes once anyway.
            var earliestByItem = unlocks
                .GroupBy(u => u.ItemName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(u => u.OccurredAt).ThenBy(u => u.RecordId).First())
                .ToList();

            var nodes = await completions.GetAutoCompletableNodes(
                userId, earliestByItem.Select(u => u.ItemName).ToList());
            if (nodes.Count == 0)
                return;

            var unlockByItem = earliestByItem.ToDictionary(u => u.ItemName, StringComparer.OrdinalIgnoreCase);

            var toAdd = new List<UserNodeCompletion>();
            var seenNodes = new HashSet<int>();
            foreach (var node in nodes)
            {
                if (!seenNodes.Add(node.NodeId)) continue;
                if (!unlockByItem.TryGetValue(node.ItemName, out var unlock)) continue;

                toAdd.Add(new UserNodeCompletion
                {
                    UserId = userId,
                    TemplateNodeId = node.NodeId,
                    CompletedAt = unlock.OccurredAt,
                    Note = await BuildNote(unlock)
                });
            }

            await completions.AddCompletions(toAdd);

            if (toAdd.Count > 0)
                logger.LogInformation("Auto-completed {Count} template node(s) for user {UserId} from drops", toAdd.Count, userId);
        }
        catch (Exception ex)
        {
            // Never let a progression-note failure break loot ingest.
            logger.LogWarning(ex, "Loot auto-completion failed for user {UserId}", userId);
        }
    }

    private async Task<string> BuildNote(FirstUnlock unlock)
    {
        // Real in-game KC when RuneLite reported it; otherwise the derived position in the
        // tracked kill log (honest wording so we never imply a true KC we don't have).
        bool realKc = unlock.RealKillCount is > 0;
        int kc = realKc
            ? unlock.RealKillCount!.Value
            : await lootRecords.GetKillOrdinal(unlock.CharacterId, unlock.SourceName, unlock.OccurredAt, unlock.RecordId);
        var kcLabel = kc <= 0
            ? null
            : realKc ? $"{kc:N0} KC" : $"{kc:N0} tracked kills";

        var date = IngestTimezone.ToZoneTime(unlock.OccurredAt).ToString("d MMM yyyy");

        // Luck verdict vs. the wiki rate, when we have a usable N/D rarity.
        var verdict = "🏆 Logged";
        var luck = "";
        var rate = await dropRates.GetRate(unlock.SourceName, unlock.ItemName);
        if (kc > 0
            && rate?.RarityNumerator is int num and > 0
            && rate.RarityDenominator is int den and > 0)
        {
            var rolls = rate.Rolls <= 0 ? 1 : rate.Rolls;
            var expected = (double)den / (num * rolls);
            if (expected > 0)
            {
                var ratio = kc / expected;
                verdict = Verdict(ratio);
                luck = $" — ~1/{Math.Round(expected):N0}, {ratio:0.0}× rate";
            }
        }

        var at = kcLabel is null ? "" : $" at {kcLabel}";
        var note = $"{verdict}: {unlock.ItemName} from {unlock.SourceName}{at}{luck} · {date}";
        return note.Length > 500 ? note[..500] : note;
    }

    private static string Verdict(double ratio) => ratio switch
    {
        <= 0.10 => "🍀 SPOONED",
        <= 0.50 => "🍀 Lucky",
        <= 1.50 => "🎯 On rate",
        <= 3.00 => "🌵 A bit dry",
        _ => "💀 Dry as the desert",
    };
}
