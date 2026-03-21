using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class LootRecord : Entity
{
    [Required]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required, StringLength(100)]
    public string SourceName { get; set; } = "";

    [Required]
    public LootSourceType SourceType { get; set; }

    public int? CombatLevel { get; set; }

    public int? KillCount { get; set; }

    [Required]
    public long TotalValue { get; set; }

    [Required, Column(TypeName = "jsonb")]
    public string DropsJson { get; set; } = "[]";

    [Required]
    public DateTimeOffset OccurredAt { get; set; }

    [StringLength(64)]
    public string? ContentHash { get; set; }

    public bool IsImported { get; set; }

    public int? GameCharacterId { get; set; }

    [ForeignKey(nameof(GameCharacterId))]
    public GameCharacter? GameCharacter { get; set; }
}
