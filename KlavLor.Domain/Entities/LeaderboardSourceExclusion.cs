using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

// Admin blacklist of sources whose items should never appear on the luck leaderboards —
// used to hide sources whose stored drop rates are wrong (e.g. shared rare-drop-table rates)
// rather than trying to correct the rate algorithmically. SourceName is the business key.
public sealed class LeaderboardSourceExclusion : Entity
{
    [Required, StringLength(100)]
    public string SourceName { get; set; } = "";
}
