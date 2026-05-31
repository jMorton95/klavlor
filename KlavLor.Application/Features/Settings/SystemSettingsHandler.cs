using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Settings;

public sealed class SystemSettingsHandler(
    ISystemSettingsRepository repository,
    ISystemSettingsCache cache,
    ICurrentUser currentUser)
{
    public async Task<Result<bool>> HandleToggleLeagues()
    {
        if (!currentUser.IsAdmin)
            return Result<bool>.Failure("Not authorized.");

        var settings = await repository.GetOrCreate();
        settings.IsLeaguesEnabled = !settings.IsLeaguesEnabled;
        await repository.Save(settings);

        cache.SetLeaguesEnabled(settings.IsLeaguesEnabled);
        return Result<bool>.Success(settings.IsLeaguesEnabled);
    }
}
