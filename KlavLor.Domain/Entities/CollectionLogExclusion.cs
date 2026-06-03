using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

/// <summary>
/// An OSRS item an admin has explicitly excluded from collection-log treatment. Kept in
/// its own table (not a flag on <see cref="CollectionLogItem"/>) so exclusions survive the
/// hourly wiki sync that wipes and rebuilds the reference set. The effective collection-log
/// set used everywhere is the synced items minus these exclusions.
/// </summary>
public sealed class CollectionLogExclusion : Entity
{
    /// <summary>In-game item id (matches CollectionLogItem.ItemId and the id on each drop).</summary>
    public int ItemId { get; set; }

    [Required, StringLength(200)]
    public string ItemName { get; set; } = "";
}
