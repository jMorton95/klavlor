using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class ApiKey : Entity
{
    [Required]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required, StringLength(64)]
    public string KeyHash { get; set; } = "";

    [Required, StringLength(8)]
    public string KeyPrefix { get; set; } = "";

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Required]
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastUsedAt { get; set; }

    [Required]
    public DateTimeOffset CreatedAt { get; set; }
}
