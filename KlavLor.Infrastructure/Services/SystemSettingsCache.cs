using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// Singleton cache of site feature flags. Writes are rare (admin toggle), reads
/// are on the hot path, so flags are stored in <c>volatile</c> fields and read
/// lock-free.
/// </summary>
internal sealed class SystemSettingsCache : ISystemSettingsCache
{
    private volatile bool _leaguesEnabled = true;

    public bool IsLeaguesEnabled => _leaguesEnabled;

    public void SetLeaguesEnabled(bool enabled) => _leaguesEnabled = enabled;
}
