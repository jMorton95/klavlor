using System.ComponentModel.DataAnnotations;
using KlavLor.Domain.Shared;

namespace KlavLor.Domain.Entities;

public sealed class Role
{
    [Required, Key]
    public int Id { get; set; }
    [Required]
    public RoleName Name { get; set; } = RoleName.User;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
