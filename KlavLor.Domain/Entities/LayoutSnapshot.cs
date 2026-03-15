using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class LayoutSnapshot : Entity
{
    [Required]
    public int TemplateId { get; set; }

    [ForeignKey(nameof(TemplateId))]
    public Template? Template { get; set; }

    [Required, Column(TypeName = "jsonb")]
    public string PositionData { get; set; } = "[]";
}
