using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class ItemIcon
{
    [Required, Key]
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string ItemName { get; set; } = "";

    public int ItemId { get; set; }

    public int? CachedImageId { get; set; }

    [ForeignKey(nameof(CachedImageId))]
    public CachedImage? CachedImage { get; set; }

    public int FailCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }
}
