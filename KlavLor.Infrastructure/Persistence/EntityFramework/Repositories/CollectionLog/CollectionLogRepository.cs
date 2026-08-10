using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.CollectionLog;

/// <summary>
/// Write side of the Temple-sourced collection log. Every write is a diff against what is already
/// stored, so an unchanged sync costs nothing and a changed one touches only the rows that moved.
/// </summary>
internal sealed class CollectionLogRepository(
    DataContext dataContext, ILogger<CollectionLogRepository> logger) : ICollectionLogRepository
{
    public async Task<List<CollectionLogSyncTarget>> GetSyncTargets()
    {
        // The RSN is the character's DisplayName — that is what an admin sets it to and what Temple
        // keys on. A character without one (auto-created on first ingest, named with a GUID) cannot
        // be looked up, so it is excluded rather than sent upstream as garbage.
        // Two straightforward queries rather than one outer join: EF could not translate a
        // GroupJoin/SelectMany over these shapes, and the roster is small enough that stitching them
        // together in memory costs nothing.
        var characters = await dataContext.GameCharacters
            .AsNoTracking()
            .Where(gc => gc.IsVisible && !gc.IsAdminHidden && gc.DisplayName != null && gc.DisplayName != "")
            .Select(gc => new { gc.Id, Rsn = gc.DisplayName! })
            .ToListAsync();

        var states = await dataContext.CharacterCollectionLogStates
            .AsNoTracking()
            .Select(s => new { s.GameCharacterId, s.TempleLastChanged, s.LastSyncedAt, s.ConsecutiveFailures })
            .ToDictionaryAsync(s => s.GameCharacterId);

        var entryCounts = await dataContext.CharacterCollectionLogEntries
            .AsNoTracking()
            .GroupBy(e => e.GameCharacterId)
            .Select(g => new { GameCharacterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GameCharacterId, x => x.Count);

        return characters
            .Select(c =>
            {
                states.TryGetValue(c.Id, out var s);
                return new CollectionLogSyncTarget(
                    c.Id, c.Rsn, s?.TempleLastChanged, s?.LastSyncedAt, s?.ConsecutiveFailures ?? 0,
                    entryCounts.TryGetValue(c.Id, out var n) ? n : 0);
            })
            // Never-synced first, then oldest — a cycle cut short still advances the whole roster.
            .OrderBy(t => t.LastSyncedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public async Task<bool> HasCategories() => await dataContext.CollectionLogCategories.AnyAsync();

    public async Task ReplaceCategories(IReadOnlyList<TempleCategory> categories)
    {
        try
        {
            await using var tx = await dataContext.Database.BeginTransactionAsync();

            // Reference data with a natural key and no dependents — a wipe-and-rebuild is simpler
            // and safer than diffing, and it lets a removed category actually disappear.
            await dataContext.CollectionLogCategoryItems.ExecuteDeleteAsync();
            await dataContext.CollectionLogCategories.ExecuteDeleteAsync();

            var now = DateTimeOffset.UtcNow;
            var order = 0;
            foreach (var category in categories)
            {
                dataContext.CollectionLogCategories.Add(new CollectionLogCategory
                {
                    Slug = category.Slug,
                    DisplayName = Humanise(category.Slug),
                    GroupName = category.GroupName,
                    ItemCount = category.ItemIds.Count,
                    SortOrder = order++,
                    SyncedAt = now
                });

                var itemOrder = 0;
                foreach (var itemId in category.ItemIds.Distinct())
                {
                    dataContext.CollectionLogCategoryItems.Add(new CollectionLogCategoryItem
                    {
                        CategorySlug = category.Slug,
                        ItemId = itemId,
                        SortOrder = itemOrder++
                    });
                }
            }

            await dataContext.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to replace collection-log categories");
            throw new RepositoryException("Failed to replace collection-log categories", ex);
        }
    }

    public async Task<CollectionLogSyncResult> ApplyPlayerLog(int gameCharacterId, TempleCollectionLog log)
    {
        try
        {
            await using var tx = await dataContext.Database.BeginTransactionAsync();
            var now = DateTimeOffset.UtcNow;

            var existing = await dataContext.CharacterCollectionLogEntries
                .Where(e => e.GameCharacterId == gameCharacterId)
                .ToDictionaryAsync(e => e.ItemId);

            var incoming = log.Items.ToDictionary(i => i.ItemId);

            // Interlock: an empty log never replaces a non-empty one. A well-formed response that
            // happens to carry no items is indistinguishable from a parse that silently produced
            // none, and the cost of being wrong is a character's whole log. Recorded as a failure so
            // it shows up rather than passing as a successful no-op.
            if (incoming.Count == 0 && existing.Count > 0)
            {
                await tx.RollbackAsync();
                await RecordSyncOutcome(gameCharacterId, log.Rsn, CollectionLogSyncOutcome.Failed,
                    $"Refused an empty log that would have cleared {existing.Count} stored entries.");
                return new CollectionLogSyncResult(0, 0, 0);
            }

            int added = 0, updated = 0;

            foreach (var (itemId, item) in incoming)
            {
                if (existing.TryGetValue(itemId, out var row))
                {
                    // Only touch a row whose facts actually moved. FirstSeenAt is never rewritten —
                    // it is our own provenance, not Temple's.
                    var changed = row.Count != item.Count || row.ObtainedAt != item.ObtainedAt;
                    row.LastSyncedAt = now;
                    if (changed)
                    {
                        row.Count = item.Count;
                        // Never overwrite a known date with a null: Temple sometimes omits a date it
                        // previously supplied, and losing it would look like the item was re-obtained.
                        row.ObtainedAt = item.ObtainedAt ?? row.ObtainedAt;
                        updated++;
                    }
                }
                else
                {
                    dataContext.CharacterCollectionLogEntries.Add(new CharacterCollectionLogEntry
                    {
                        GameCharacterId = gameCharacterId,
                        ItemId = itemId,
                        Count = item.Count,
                        ObtainedAt = item.ObtainedAt,
                        FirstSeenAt = now,
                        LastSyncedAt = now
                    });
                    added++;
                }
            }

            // An entry the upstream no longer reports. Rare and usually a Jagex content change
            // rather than a player losing an item, but leaving it would inflate the count forever.
            var removed = existing.Values.Where(e => !incoming.ContainsKey(e.ItemId)).ToList();
            if (removed.Count > 0)
                dataContext.CharacterCollectionLogEntries.RemoveRange(removed);

            await UpsertState(gameCharacterId, log, now, changedEntries: added + updated + removed.Count > 0);
            await dataContext.SaveChangesAsync();
            await tx.CommitAsync();

            return new CollectionLogSyncResult(added, updated, removed.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply collection log for character {CharacterId}", gameCharacterId);
            throw new RepositoryException("Failed to apply collection log", ex);
        }
    }

    public async Task RecordSyncOutcome(int gameCharacterId, string rsn, CollectionLogSyncOutcome outcome, string? error)
    {
        var state = await dataContext.CharacterCollectionLogStates
            .FirstOrDefaultAsync(s => s.GameCharacterId == gameCharacterId);

        if (state is null)
        {
            state = new CharacterCollectionLogState { GameCharacterId = gameCharacterId, Rsn = rsn };
            dataContext.CharacterCollectionLogStates.Add(state);
        }

        // Deliberately does NOT touch the entries. A player who stops syncing to Temple keeps the
        // log we already hold; the state row is what tells the UI it has gone stale.
        state.Rsn = rsn;
        state.LastSyncedAt = DateTimeOffset.UtcNow;
        state.LastOutcome = outcome;
        state.LastError = Truncate(error, 300);
        state.ConsecutiveFailures++;

        await dataContext.SaveChangesAsync();
    }

    public async Task RecordUnchanged(int gameCharacterId, DateTimeOffset? templeLastChecked)
    {
        var state = await dataContext.CharacterCollectionLogStates
            .FirstOrDefaultAsync(s => s.GameCharacterId == gameCharacterId);
        if (state is null) return;

        state.LastSyncedAt = DateTimeOffset.UtcNow;
        state.TempleLastChecked = templeLastChecked ?? state.TempleLastChecked;
        state.LastOutcome = CollectionLogSyncOutcome.Unchanged;
        state.LastError = null;
        state.ConsecutiveFailures = 0;

        await dataContext.SaveChangesAsync();
    }

    private async Task UpsertState(int gameCharacterId, TempleCollectionLog log, DateTimeOffset now, bool changedEntries)
    {
        var state = await dataContext.CharacterCollectionLogStates
            .FirstOrDefaultAsync(s => s.GameCharacterId == gameCharacterId);

        if (state is null)
        {
            state = new CharacterCollectionLogState { GameCharacterId = gameCharacterId };
            dataContext.CharacterCollectionLogStates.Add(state);
        }

        state.Rsn = log.Rsn;
        state.TempleDisplayName = log.DisplayName;
        state.GameMode = log.GameMode;
        // Trust our own row count over Temple's header when they disagree — the rows are what every
        // page renders, so a header that contradicted them would be visibly wrong.
        state.TotalObtained = log.Items.Count;
        state.TotalAvailable = log.TotalAvailable;
        state.CategoriesFinished = log.CategoriesFinished;
        state.CategoriesAvailable = log.CategoriesAvailable;
        state.HiscoresRank = log.HiscoresRank;
        state.TempleLastChecked = log.LastChecked;
        state.TempleLastChanged = log.LastChanged;
        state.LastSyncedAt = now;
        if (changedEntries) state.LastChangedAt = now;
        state.LastOutcome = CollectionLogSyncOutcome.Ok;
        state.LastError = null;
        state.ConsecutiveFailures = 0;
    }

    /// <summary>"abyssal_sire" → "Abyssal Sire". Temple has no display names, only slugs.</summary>
    private static string Humanise(string slug) =>
        string.Join(' ', slug.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 2 ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
}
