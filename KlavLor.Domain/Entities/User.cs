using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

public sealed class User : Entity
{
    public User(string firstName, string lastName, string email, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new DomainException("First name cannot be empty.");
        if (string.IsNullOrWhiteSpace(lastName)) throw new DomainException("Last name cannot be empty.");
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email cannot be empty.");

        FirstName = firstName;
        LastName = lastName;
        Email = email;
        IsActive = isActive;
    }

    [Required, StringLength(50)]
    public string FirstName { get; set; }

    [Required, StringLength(50)]
    public string LastName { get; set; }

    [Required, StringLength(255)]
    public string Email { get; set; }

    [Required]
    public bool IsActive { get; set; }

    [Required, StringLength(255)]
    public string? HashedPassword { get; set; }

    public bool IsLockedOut { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public int AccessFailedCount { get; set; }

    private readonly List<UserRole> _userRoles = [];

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    public void UpdateProfile(string firstName, string lastName, string email, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new DomainException("First name cannot be empty.");
        if (string.IsNullOrWhiteSpace(lastName)) throw new DomainException("Last name cannot be empty.");
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email cannot be empty.");

        FirstName = firstName;
        LastName = lastName;
        Email = email;
        IsActive = isActive;
    }

    public void AssignRole(Role role)
    {
        if(_userRoles.Any(ur => ur.RoleId == role.Id))
        {
            throw new DomainException("User already has this role assigned.");
        }

        _userRoles.Add(new UserRole { User = this, Role = role });
    }

    public void UnassignRole(Role role)
    {
        _userRoles.Remove(_userRoles.Single(ur => ur.RoleId == role.Id));
    }
}
