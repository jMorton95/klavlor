using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

/// <summary>
/// A loot source confirmed (by a wiki fetch) to have no drop-rate data. Recorded so the
/// admin "missing rates" backlog can hide it by default — there's nothing to fetch — until
/// an admin explicitly reveals and re-checks it. Cleared automatically if a later fetch
/// does find data.
/// </summary>
public sealed class DropRateMiss : Entity
{
    [Required, StringLength(150)]
    public string SourceName { get; set; } = "";
}
