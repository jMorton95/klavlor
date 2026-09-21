using System.Text.Json;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot;

/// <summary>
/// THE one way to read a loot record's drops back out of its canonical <c>DropsJson</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>DropsJson</c> is the permanent, raw record of what RuneLite reported, and it is never
/// rewritten — that is what makes both admin decisions below reversible. It is therefore NOT what
/// the site shows. Two things sit between the stored JSON and the truth, and both of them are
/// already applied to the <c>LootDrops</c> projection that every SQL read site queries:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Intrinsic item values.</b> An untradeable arrives priced at 0 GP; the admin's override is the
/// effective price. See <see cref="IItemValueOverrideCache"/>.
/// </description></item>
/// <item><description>
/// <b>Blacklisted drops.</b> An item an admin has decided is not loot at all is absent from the
/// projection entirely. See <see cref="BlacklistedLootDrop"/>.
/// </description></item>
/// </list>
/// <para>
/// A site that deserialises <c>DropsJson</c> and skips either of these disagrees with the stored
/// projection about the same kill, and the drop then reads one way live and another way after a
/// refresh. That has shipped twice — once for prices, once for the projection rebuild — so the two
/// rules are applied together here rather than left as two things every call site must remember.
/// Deserialisation lives here for the same reason: there is no way to get the JSON without going
/// through the filter.
/// </para>
/// <para>
/// Singleton, wrapping two singleton caches. No allocation in the common case: with nothing
/// overridden and nothing blacklisted the deserialised list is returned as-is.
/// </para>
/// </remarks>
public sealed class EffectiveDropReader(IItemValueOverrideCache prices, IDropBlacklistCache blacklist)
{
    /// <summary>
    /// Deserialise one record's drops and apply both admin decisions.
    /// </summary>
    /// <param name="lootRecordId">
    /// The record the JSON came from. The blacklist is per (record, item), so this must be the real
    /// id. Pass 0 for a record that does not exist yet — a drop on an unsaved record cannot have
    /// been blacklisted, and the lookup is a miss.
    /// </param>
    public List<LootDrop> Read(int lootRecordId, string? dropsJson)
    {
        if (string.IsNullOrWhiteSpace(dropsJson)) return [];

        List<LootDrop> drops;
        try
        {
            drops = JsonSerializer.Deserialize<List<LootDrop>>(dropsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }

        return Apply(lootRecordId, drops);
    }

    /// <summary>
    /// Apply both decisions to an already-deserialised list — for the ingest path, which holds the
    /// drops in memory before any JSON exists.
    /// </summary>
    public List<LootDrop> Apply(int lootRecordId, List<LootDrop> drops)
    {
        if (drops.Count == 0) return drops;

        // Blacklist first: a removed drop needs no price. Only walks the list when something,
        // anywhere, is blacklisted.
        if (blacklist.HasAny)
        {
            List<LootDrop>? kept = null;
            for (var i = 0; i < drops.Count; i++)
            {
                if (!blacklist.IsBlacklisted(lootRecordId, drops[i].ItemId, drops[i].Name))
                {
                    kept?.Add(drops[i]);
                    continue;
                }

                // First removal: copy everything kept so far, then skip this one.
                kept ??= [.. drops.Take(i)];
            }

            drops = kept ?? drops;
        }

        return prices.WithEffectivePrices(drops);
    }
}
