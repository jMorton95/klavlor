using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class TemplateEdge : Entity
{
    [Required]
    public int TemplateId { get; set; }

    [ForeignKey(nameof(TemplateId))]
    public Template? Template { get; set; }

    [Required]
    public int FromNodeId { get; set; }

    [ForeignKey(nameof(FromNodeId))]
    public TemplateNode? FromNode { get; set; }

    [Required]
    public int ToNodeId { get; set; }

    [ForeignKey(nameof(ToNodeId))]
    public TemplateNode? ToNode { get; set; }
}
