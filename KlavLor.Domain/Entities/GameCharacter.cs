using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class GameCharacter : Entity
{
    [Required]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required, StringLength(100)]
    public string RuneLiteId { get; set; } = "";

    [StringLength(100)]
    public string? DisplayName { get; set; }

    public bool IsVisible { get; set; }

    public bool IsAdminHidden { get; set; }

    public string GetEffectiveName(string? userName = null) => DisplayName ?? userName ?? RuneLiteId;
}
