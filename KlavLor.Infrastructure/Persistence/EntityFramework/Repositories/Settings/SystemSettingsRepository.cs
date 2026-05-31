using Microsoft.EntityFrameworkCore;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Settings;

internal sealed class SystemSettingsRepository(DataContext dataContext) : ISystemSettingsRepository
{
    public async Task<SystemSettings> GetOrCreate()
    {
        var settings = await dataContext.SystemSettings.SingleOrDefaultAsync();
        if (settings is not null) return settings;

        settings = new SystemSettings { IsLeaguesEnabled = true };
        dataContext.SystemSettings.Add(settings);
        await dataContext.SaveChangesAsync();
        return settings;
    }

    public async Task Save(SystemSettings settings)
    {
        dataContext.SystemSettings.Update(settings);
        await dataContext.SaveChangesAsync();
    }
}
