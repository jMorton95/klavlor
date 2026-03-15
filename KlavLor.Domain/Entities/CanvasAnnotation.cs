using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class CanvasAnnotation : Entity
{
    [Required]
    public int TemplateId { get; set; }

    [ForeignKey(nameof(TemplateId))]
    public Template? Template { get; set; }

    [Required, StringLength(500)]
    public string Text { get; set; } = "";

    [Required]
    public double PositionX { get; set; }

    [Required]
    public double PositionY { get; set; }

    [Required, StringLength(10)]
    public string FontSize { get; set; } = "medium";
}
