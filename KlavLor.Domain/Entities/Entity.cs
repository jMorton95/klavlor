using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public abstract class Entity
{
    [Required, Key]
    public int Id { get; set; }

    [Required]
    public uint RowVersion { get; set; }

    [Required]
    public DateTimeOffset SavedAt { get; set; }

    public int? SavedById { get; set; }

    [ForeignKey(nameof(SavedById))]
    public User? SavedBy { get; set; }
}
