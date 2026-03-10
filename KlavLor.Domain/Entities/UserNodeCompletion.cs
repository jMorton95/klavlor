using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class UserNodeCompletion
{
    [Key, Column(Order = 0)]
    public int UserId { get; set; }

    [Key, Column(Order = 1)]
    public int TemplateNodeId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(TemplateNodeId))]
    public TemplateNode? TemplateNode { get; set; }

    [Required]
    public DateTimeOffset CompletedAt { get; set; }
}
