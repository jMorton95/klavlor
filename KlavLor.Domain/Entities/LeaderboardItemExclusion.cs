using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

// Admin blacklist of items that should never appear on the luck leaderboards, regardless of
// which source they came from — the item-level counterpart to LeaderboardSourceExclusion. Used
// when a single item (e.g. a shared rare-drop-table drop) is dropped by many sources and its
// leaderboard entries are noise everywhere. ItemName is the business key.
public sealed class LeaderboardItemExclusion : Entity
{
    [Required, StringLength(100)]
    public string ItemName { get; set; } = "";
}
