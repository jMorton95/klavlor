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
        SecurityStamp = Guid.NewGuid().ToString("N");
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

    // Opaque token embedded in the auth cookie at login and re-checked server-side on an interval.
    // Regenerating it invalidates every outstanding session for this user.
    [Required, StringLength(64)]
    public string SecurityStamp { get; private set; }

    private readonly List<UserRole> _userRoles = [];

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    public void UpdateProfile(string firstName, string lastName, string email, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new DomainException("First name cannot be empty.");
        if (string.IsNullOrWhiteSpace(lastName)) throw new DomainException("Last name cannot be empty.");
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email cannot be empty.");

        // Deactivating a user must terminate their existing sessions, not just block future logins.
        if (IsActive && !isActive)
            InvalidateSessions();

        FirstName = firstName;
        LastName = lastName;
        Email = email;
        IsActive = isActive;
    }

    // Role add/remove deliberately does NOT bump the security stamp: the cookie validator syncs a
    // user's role claims from the database on its normal revalidation interval, so grants/revocations
    // take effect without forcing a logout. (Revocation timing is unchanged — it still lands at that
    // same revalidation point.) The stamp is reserved for hard invalidation: deactivation,
    // password changes, and explicit "sign out everywhere".
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

    // Regenerates the security stamp, hard-invalidating all outstanding sessions for this user on
    // their next server-side revalidation. Call on deactivation, password resets, or an explicit
    // "sign out everywhere". (Role changes do NOT use this — see AssignRole/UnassignRole.)
    public void InvalidateSessions() => SecurityStamp = Guid.NewGuid().ToString("N");
}
