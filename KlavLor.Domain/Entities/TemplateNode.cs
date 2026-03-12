using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class TemplateNode : Entity
{
    [Required]
    public int TemplateId { get; set; }

    [ForeignKey(nameof(TemplateId))]
    public Template? Template { get; set; }

    [Required, StringLength(100)]
    public string Label { get; set; } = "";

    [Required]
    public NodeType NodeType { get; set; }

    public int? GroupId { get; set; }

    [ForeignKey(nameof(GroupId))]
    public TemplateNodeGroup? Group { get; set; }

    public int? GearItemId { get; set; }

    [ForeignKey(nameof(GearItemId))]
    public GearItem? GearItem { get; set; }

    [Required]
    public double PositionX { get; set; }

    [Required]
    public double PositionY { get; set; }

    public string? IconUrl { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Metadata { get; set; }

    [Required]
    public int SortOrder { get; set; }

    [StringLength(20)]
    public string Color { get; set; } = "amber";
}
