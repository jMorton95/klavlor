using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Web.Application.Features.Loot.Feed;

/// <summary>
/// Per-tier presentation and routing, in one place. The five swimlanes used to be five
/// near-identical copy-pasted blocks in LootFeedGrid; now that each tier loads through its own
/// API call there is a single column component, and this supplies what differs between them.
/// </summary>
public static class TierStyle
{
    public sealed record Style(
        string Slug,
        string Label,
        string Range,
        string HeaderClass,
        string EmptyBorderClass);

    public static Style For(LootFeedTier tier) => tier switch
    {
        LootFeedTier.Standard => new("standard", "Standard", "10K – 100K",
            "text-slate-700 dark:text-slate-300", "border-slate-200 dark:border-slate-700"),
        LootFeedTier.Uncommon => new("uncommon", "Uncommon", "100K – 1M",
            "text-green-600 dark:text-green-400", "border-green-200 dark:border-green-900"),
        LootFeedTier.Rare => new("rare", "Rare", "1M – 10M",
            "text-blue-600 dark:text-blue-400", "border-blue-200 dark:border-blue-900"),
        LootFeedTier.Epic => new("epic", "Epic", "10M – 100M",
            "text-purple-600 dark:text-purple-400", "border-purple-200 dark:border-purple-900"),
        LootFeedTier.Legendary => new("legendary", "Legendary", "100M+",
            "text-amber-600 dark:text-amber-400", "border-amber-200 dark:border-amber-900"),
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };

    /// <summary>The SSE stream route for a tier within a scope.</summary>
    public static string StreamRoute(LootFeedTier tier, LootFeedScope scope) =>
        scope == LootFeedScope.Leagues
            ? tier switch
            {
                LootFeedTier.Standard => AppRoutes.LootFeedLeaguesStreamStandard,
                LootFeedTier.Uncommon => AppRoutes.LootFeedLeaguesStreamUncommon,
                LootFeedTier.Rare => AppRoutes.LootFeedLeaguesStreamRare,
                LootFeedTier.Epic => AppRoutes.LootFeedLeaguesStreamEpic,
                LootFeedTier.Legendary => AppRoutes.LootFeedLeaguesStreamLegendary,
                _ => throw new ArgumentOutOfRangeException(nameof(tier))
            }
            : tier switch
            {
                LootFeedTier.Standard => AppRoutes.LootFeedStreamStandard,
                LootFeedTier.Uncommon => AppRoutes.LootFeedStreamUncommon,
                LootFeedTier.Rare => AppRoutes.LootFeedStreamRare,
                LootFeedTier.Epic => AppRoutes.LootFeedStreamEpic,
                LootFeedTier.Legendary => AppRoutes.LootFeedStreamLegendary,
                _ => throw new ArgumentOutOfRangeException(nameof(tier))
            };

    /// <summary>The per-tier column endpoint a shell fetches its own contents from.</summary>
    public static string ColumnRoute(LootFeedTier tier, LootFeedScope scope) =>
        (scope == LootFeedScope.Leagues ? AppRoutes.LootFeedLeaguesColumn : AppRoutes.LootFeedColumn)
            .Replace("{tier}", For(tier).Slug);
}
