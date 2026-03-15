using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class CanvasRegion : Entity
{
    [Required]
    public int TemplateId { get; set; }

    [ForeignKey(nameof(TemplateId))]
    public Template? Template { get; set; }

    [StringLength(100)]
    public string? Label { get; set; }

    [Required]
    public double PositionX { get; set; }

    [Required]
    public double PositionY { get; set; }

    [Required]
    public double Width { get; set; }

    [Required]
    public double Height { get; set; }

    [Required, StringLength(20)]
    public string Color { get; set; } = "slate";

    [Required]
    public double Opacity { get; set; } = 0.15;
}
