using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

public sealed class GearItem : Entity
{
    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? WikiUrl { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    [Required]
    public NodeType ItemType { get; set; }
}
