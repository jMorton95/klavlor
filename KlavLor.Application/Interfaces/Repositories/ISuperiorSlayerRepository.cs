using KlavLor.Application.Features.Loot.Superiors;

namespace KlavLor.Application.Interfaces.Repositories;

/// <summary>
/// The Superior Slayer comparison's two reads. Both aggregate over visible characters only, matching
/// the public loot surface (feed, character logs, global source pages).
/// </summary>
/// <remarks>
/// Both take their source names LOWERCASED and compare against a lowercased column. The caller
/// passes the registry's lists; the repository does not reach into the registry itself, so the
/// queries stay plain filters and a test can drive them with its own names.
/// </remarks>
public interface ISuperiorSlayerRepository
{
    /// <summary>
    /// Kills per (visible character, superior), with when they first and last killed it. Only pairs
    /// with at least one record come back - the handler fills the rest of the matrix in.
    /// </summary>
    Task<List<SuperiorCountRow>> GetCounts(IReadOnlyCollection<string> loweredSourceNames);

    /// <summary>
    /// Kills of each ORDINARY monster, PER visible character — the grind each player's superior
    /// count sits on top of. Only pairs with something recorded come back.
    /// </summary>
    Task<List<SuperiorBaseKillRow>> GetBaseMonsterKills(IReadOnlyCollection<string> loweredBaseNames);

    /// <summary>
    /// Every unique-table item ever received from a superior, with the monster it came from, who got
    /// it and when. Unbounded by time on purpose - there are a couple of dozen of these in total and
    /// they are the point of the category, so none of them ages out.
    /// </summary>
    Task<List<SuperiorUniqueDrop>> GetUniqueDrops(
        IReadOnlyCollection<string> loweredSourceNames,
        IReadOnlyCollection<string> loweredItemNames);
}
