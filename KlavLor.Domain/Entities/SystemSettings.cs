namespace KlavLor.Domain.Entities;

/// <summary>
/// Single-row table holding site-wide feature flags. The row is created on first
/// access by the repository if missing, and mirrored in an in-memory cache that
/// is consulted on the hot read path instead of hitting the database.
/// </summary>
public sealed class SystemSettings : Entity
{
    public bool IsLeaguesEnabled { get; set; } = true;
}
