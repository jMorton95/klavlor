using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class SourceIcon
{
    [Required, Key]
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string SourceName { get; set; } = "";

    public int? CachedImageId { get; set; }

    [ForeignKey(nameof(CachedImageId))]
    public CachedImage? CachedImage { get; set; }

    public int FailCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }
}
