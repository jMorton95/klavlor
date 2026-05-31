using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface ISystemSettingsRepository
{
    /// <summary>Returns the single settings row, creating it with defaults on first access.</summary>
    Task<SystemSettings> GetOrCreate();

    Task Save(SystemSettings settings);
}
