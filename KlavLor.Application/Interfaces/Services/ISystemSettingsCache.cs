namespace KlavLor.Application.Interfaces.Services;

/// <summary>
/// Process-wide cache of site feature flags. Primed once at startup and refreshed
/// only when an admin flips a toggle — read on the hot path (sidebar, character
/// pages, feed endpoints) so we avoid a DB round-trip per request.
/// </summary>
public interface ISystemSettingsCache
{
    bool IsLeaguesEnabled { get; }

    void SetLeaguesEnabled(bool enabled);
}
