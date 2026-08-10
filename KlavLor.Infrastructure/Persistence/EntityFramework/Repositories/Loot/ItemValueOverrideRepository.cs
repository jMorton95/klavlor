using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using KlavLor.Application.Features.Loot.ItemValues;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class ItemValueOverrideRepository(
    DataContext dataContext,
    IItemValueOverrideCache cache,
    ICollectionLogCache collectionLogCache) : IItemValueOverrideRepository
{
    // Records are re-derived in bounded batches so a rebuild over a heavily-dropped item never
    // holds one long transaction open.
    private const int BatchSize = 500;

    public async Task<List<ItemValueOverrideRow>> List()
    {
        var overrides = await dataContext.ItemValueOverrides
            .AsNoTracking()
            .OrderBy(o => o.ItemName)
            .Select(o => new { o.ItemId, o.ItemName, o.Value })
            .ToListAsync();

        if (overrides.Count == 0) return [];

        var ids = overrides.Select(o => o.ItemId).ToList();
        var counts = await dataContext.LootDrops
            .AsNoTracking()
            .Where(d => ids.Contains(d.ItemId))
            .GroupBy(d => d.ItemId)
            .Select(g => new { ItemId = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count);

        return overrides
            .Select(o => new ItemValueOverrideRow(
                o.ItemId, o.ItemName, o.Value, counts.TryGetValue(o.ItemId, out var c) ? c : 0))
            .ToList();
    }

    public async Task<List<ItemValueCandidate>> SearchItems(string? term, int limit)
    {
        if (string.IsNullOrWhiteSpace(term)) return [];

        // Search the items that have actually been dropped rather than the collection log: the
        // untradeables this feature exists for are ordinary drops, and some aren't clog entries.
        var pattern = $"%{term.Trim()}%";
        var candidates = await dataContext.LootDrops
            .AsNoTracking()
            .Where(d => EF.Functions.ILike(d.Name, pattern))
            .GroupBy(d => new { d.ItemId, d.Name })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.Name,
                StoredPrice = g.Max(x => x.Price),
                Count = g.LongCount()
            })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync();

        if (candidates.Count == 0) return [];

        var ids = candidates.Select(c => c.ItemId).ToList();
        var configured = await dataContext.ItemValueOverrides
            .AsNoTracking()
            .Where(o => ids.Contains(o.ItemId))
            .ToDictionaryAsync(o => o.ItemId, o => o.Value);

        return candidates
            .Select(c => new ItemValueCandidate(
                c.ItemId,
                c.Name,
                c.StoredPrice,
                c.Count,
                configured.TryGetValue(c.ItemId, out var v) ? v : null))
            .ToList();
    }

    public async Task<List<ZeroValueItem>> FindZeroValueItems(int limit)
    {
        // MAX(Price) = 0 rather than Price = 0 per row: an item that has ever been recorded with a
        // real price isn't a candidate, it just happened to drop as part of a zero-priced stack.
        // Excludes items that already have an override, since those read as non-zero anyway.
        var rows = await dataContext.LootDrops
            .AsNoTracking()
            .GroupBy(d => new { d.ItemId, d.Name })
            .Where(g => g.Max(x => x.Price) == 0)
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.Name,
                DropCount = g.LongCount()
            })
            .OrderByDescending(x => x.DropCount)
            .Take(limit)
            .ToListAsync();

        // Collection-log membership comes from the in-memory cache rather than a join: the whole
        // point of the flag is to sort the interesting items to the top, and the cache is already
        // the authority the ingest path classifies against.
        return rows
            .Select(r => new ZeroValueItem(
                r.ItemId, r.Name, r.DropCount, collectionLogCache.IsCollectionLogItem(r.ItemId, r.Name)))
            .OrderByDescending(r => r.IsCollectionLogItem)
            .ThenByDescending(r => r.DropCount)
            .ToList();
    }

    public async Task Upsert(int itemId, string itemName, int value)
    {
        var existing = await dataContext.ItemValueOverrides.FirstOrDefaultAsync(o => o.ItemId == itemId);
        if (existing is null)
        {
            dataContext.ItemValueOverrides.Add(new ItemValueOverride
            {
                ItemId = itemId,
                ItemName = itemName,
                Value = value
            });
        }
        else
        {
            existing.ItemName = itemName;
            existing.Value = value;
        }

        await dataContext.SaveChangesAsync();
    }

    public async Task Delete(int itemId)
    {
        await dataContext.ItemValueOverrides
            .Where(o => o.ItemId == itemId)
            .ExecuteDeleteAsync();
    }

    public async Task<IReadOnlyList<ItemValueOverrideValue>> GetAll()
    {
        return await dataContext.ItemValueOverrides
            .AsNoTracking()
            .Select(o => new ItemValueOverrideValue(o.ItemId, o.ItemName, o.Value))
            .ToListAsync();
    }

    public async Task<ItemValueRebuildResult> RebuildForItem(int itemId)
    {
        // Backed by IX_LootDrops_ItemId, so this stays a bounded, indexed set even on a big table.
        var recordIds = await dataContext.LootDrops
            .AsNoTracking()
            .Where(d => d.ItemId == itemId)
            .Select(d => d.LootRecordId)
            .Distinct()
            .ToListAsync();

        if (recordIds.Count == 0)
            return new ItemValueRebuildResult(0, [], [], []);

        var characterIds = new HashSet<int>();
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        var itemNames = new HashSet<string>(StringComparer.Ordinal);
        var updated = 0;

        for (var offset = 0; offset < recordIds.Count; offset += BatchSize)
        {
            var batch = recordIds.GetRange(offset, Math.Min(BatchSize, recordIds.Count - offset));

            var records = await dataContext.LootRecords
                .AsNoTracking()
                .Where(r => batch.Contains(r.Id))
                .Select(r => new { r.Id, r.DropsJson, r.SourceName, r.GameCharacterId })
                .ToListAsync();

            // Tracked, because these are the rows being rewritten. LootDropRow deliberately does not
            // extend Entity, so saving them churns no audit columns and no concurrency token.
            var rows = await dataContext.LootDrops
                .Where(d => batch.Contains(d.LootRecordId))
                .OrderBy(d => d.LootRecordId).ThenBy(d => d.Id)
                .ToListAsync();
            var rowsByRecord = rows.GroupBy(d => d.LootRecordId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var record in records)
            {
                if (!rowsByRecord.TryGetValue(record.Id, out var recordRows)) continue;

                var raw = ParseDrops(record.DropsJson);
                var changedHere = false;

                for (var i = 0; i < recordRows.Count; i++)
                {
                    var row = recordRows[i];
                    // DropsJson and the projection are written from the same list in the same order
                    // (LootIngestHandler.FinalizeDrops), and rows come back in insertion order, so
                    // index alignment is exact. Fall back to an id match if a legacy record's two
                    // representations ever disagree in length.
                    var rawPrice = i < raw.Count && raw[i].ItemId == row.ItemId
                        ? raw[i].Price
                        : raw.FirstOrDefault(d => d.ItemId == row.ItemId)?.Price ?? row.Price;

                    var effective = cache.GetPrice(row.ItemId, rawPrice);
                    if (row.Price == effective) continue;

                    row.Price = effective;
                    changedHere = true;
                    itemNames.Add(row.Name);
                }

                if (!changedHere) continue;

                updated++;
                sourceNames.Add(record.SourceName);
                if (record.GameCharacterId is { } cid) characterIds.Add(cid);
            }

            await dataContext.SaveChangesAsync();

            // Roll the per-record totals up from the rows just written. Set-based and raw, so the
            // LootRecords rows are not tracked and pick up no audit or RowVersion churn — TotalValue
            // is a derived projection, not a user edit.
            await RecomputeTotals(batch);
        }

        return new ItemValueRebuildResult(updated, [.. characterIds], [.. sourceNames], [.. itemNames]);
    }

    private async Task RecomputeTotals(List<int> recordIds)
    {
        const string sql = """
            UPDATE "LootRecords" lr
            SET "TotalValue" = agg.total
            FROM (
                SELECT "LootRecordId", COALESCE(SUM("Quantity"::bigint * "Price"::bigint), 0) AS total
                FROM "LootDrops"
                WHERE "LootRecordId" = ANY(@ids)
                GROUP BY "LootRecordId"
            ) agg
            WHERE lr."Id" = agg."LootRecordId" AND lr."TotalValue" IS DISTINCT FROM agg.total
            """;

        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@ids", recordIds.ToArray()));
        await cmd.ExecuteNonQueryAsync();
    }

    private static List<LootDrop> ParseDrops(string dropsJson)
    {
        if (string.IsNullOrWhiteSpace(dropsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<LootDrop>>(dropsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
