using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Features.Loot.Summary;

/// <summary>
/// The signed-in user's monthly summary: one month, seen through one of their own characters — by
/// default the one they played most that month.
///
/// Computed on request from the loot tables and never stored. Each panel of the page is its own
/// request and resolves the month and character for itself, so the page shell can paint without a
/// query; the per-user resolution is memoised briefly so those requests share one read.
/// </summary>
public sealed class MonthlySummaryHandler(
    ILootMonthlySummaryRepository summaryRepository,
    ILootProfileRepository profileRepository,
    ICurrentUser currentUser,
    IPetCache petCache,
    IMemoryCache cache)
{
    public const int CollectionLimit = 60;
    public const int SourceLimit = 12;
    public const int DropLimit = 12;

    /// <summary>
    /// A notable drop is one the live feed would have published. The floor is also what keeps the
    /// query cheap: without it, ranking a month's drops scanned every row of LootDrops.
    /// </summary>
    public static long NotableFloor => ILootFeedService.GetTierRange(LootFeedTier.Standard).Min;

    // Not versioned like LootStatsCache, because it spans every character the user owns and the
    // version is per character. Long enough for one page view's panels and widget to share a single
    // read; short enough that a kill moving the primary character shows up within a minute.
    private static readonly TimeSpan ActivityTtl = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The header teaser: last month, through the primary character. Null when there is nothing to
    /// show, or when the user dismissed this month's widget.
    /// </summary>
    public async Task<SummaryWidget?> Widget(string? dismissedMonth)
    {
        if (currentUser.UserId is not { } userId) return null;

        if (SummaryWidget.MonthToOffer(dismissedMonth, DateTimeOffset.UtcNow) is not { } month) return null;

        var primary = (await Activity(userId, month)).Primary;
        return primary is null ? null : new SummaryWidget(month, primary);
    }

    public async Task<Result<SummaryOverview>> Overview(string? month, int? characterId)
    {
        if (await Resolve(month, characterId) is not (var userId, var context))
            return Result<SummaryOverview>.Failure("Not signed in.");
        if (context.Character is not { } character)
            return Result<SummaryOverview>.Success(new SummaryOverview(context, null, []));

        // Sequential awaits — one scoped DbContext, one query at a time.
        SummaryCharacter? previous = null;
        if (context.HasPrevious)
        {
            var before = await Activity(userId, context.Month.Previous());
            previous = before.Characters.FirstOrDefault(c => c.CharacterId == character.CharacterId);
        }

        var days = await CachedForCharacter(character.CharacterId, "Calendar", context.Month, () =>
            profileRepository.GetActivityCalendar(character.CharacterId, context.Month.StartUtc, context.Month.EndUtc));

        return Result<SummaryOverview>.Success(new SummaryOverview(context, previous, days));
    }

    public async Task<Result<SummaryCollection?>> Collection(string? month, int? characterId)
    {
        if (await Resolve(month, characterId) is not (_, var context))
            return Result<SummaryCollection?>.Failure("Not signed in.");
        if (context.Character is not { } character)
            return Result<SummaryCollection?>.Success(null);

        var page = await CachedForCharacter(character.CharacterId, "Collection", context.Month, () =>
            summaryRepository.GetNewCollectionLogItems(
                character.CharacterId, context.Month.StartUtc, context.Month.EndUtc, CollectionLimit));

        // Pet-ness is looked up rather than cached with the page: the pet list can be refreshed
        // while an entry is live, and the lookup is a hash probe.
        var marked = page with
        {
            Entries = page.Entries.Select(e => e with { IsPet = petCache.IsPet(e.ItemId, e.ItemName) }).ToList()
        };
        return Result<SummaryCollection?>.Success(new SummaryCollection(context, marked));
    }

    public async Task<Result<SummarySources?>> Sources(string? month, int? characterId)
    {
        if (await Resolve(month, characterId) is not (_, var context))
            return Result<SummarySources?>.Failure("Not signed in.");
        if (context.Character is not { } character)
            return Result<SummarySources?>.Success(null);

        var page = await CachedForCharacter(character.CharacterId, "Sources", context.Month, () =>
            summaryRepository.GetSources(character.CharacterId, context.Month.StartUtc, context.Month.EndUtc, SourceLimit));
        return Result<SummarySources?>.Success(new SummarySources(context, page));
    }

    public async Task<Result<SummaryDrops?>> Drops(string? month, int? characterId)
    {
        if (await Resolve(month, characterId) is not (_, var context))
            return Result<SummaryDrops?>.Failure("Not signed in.");
        if (context.Character is not { } character)
            return Result<SummaryDrops?>.Success(null);

        var drops = await CachedForCharacter(character.CharacterId, "Drops", context.Month, () =>
            summaryRepository.GetNotableDrops(
                character.CharacterId, context.Month.StartUtc, context.Month.EndUtc, NotableFloor, DropLimit));
        return Result<SummaryDrops?>.Success(new SummaryDrops(context, drops, NotableFloor));
    }

    private async Task<(int UserId, SummaryContext Context)?> Resolve(string? monthSlug, int? characterId)
    {
        if (currentUser.UserId is not { } userId) return null;

        var now = DateTimeOffset.UtcNow;
        var month = SummaryMonth.Resolve(monthSlug, now);
        var activity = await Activity(userId, month);
        var primary = activity.Primary;

        // A named character is honoured only when it is one of the user's OWN — the activity list is
        // exactly those, so membership is the ownership check. Anything else (someone else's id, a
        // deleted character, a stale link) falls back to the primary rather than failing, because
        // the id arrives off a query string. A summary is never a way to read another user's month.
        var requested = characterId is { } id
            ? activity.Characters.FirstOrDefault(c => c.CharacterId == id)
            : null;
        var selected = requested ?? primary;

        var context = new SummaryContext(
            month,
            SummaryMonth.Containing(now),
            activity.FirstActivityAt is { } first ? SummaryMonth.Containing(first) : null,
            selected,
            IsPrimary: selected is not null && selected.CharacterId == primary?.CharacterId,
            CharacterRequested: requested is not null,
            activity.Characters.Where(c => c.WasActive).ToList());

        return (userId, context);
    }

    private async Task<SummaryActivity> Activity(int userId, SummaryMonth month)
    {
        var key = $"monthly-summary:activity:{userId}:{month.Slug}";
        var activity = await cache.GetOrCreateAsync(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ActivityTtl;
            return summaryRepository.GetActivity(userId, month.StartUtc, month.EndUtc);
        });
        return activity!;
    }

    // Per-character panels ride LootStatsCache's versioning, so an ingest, a record-audit change, an
    // item-value override or a special-loot injection for the character makes them unreachable.
    private async Task<T> CachedForCharacter<T>(int characterId, string panel, SummaryMonth month, Func<Task<T>> load)
    {
        var version = LootStatsCache.GetVersion(cache, characterId);
        var key = LootStatsCache.EntryKey(characterId, version, $"MonthlySummary.{panel}", month.Slug);
        var value = await cache.GetOrCreateAsync(key, entry =>
        {
            entry.SlidingExpiration = LootStatsCache.EntryTtl;
            return load();
        });
        return value!;
    }
}
