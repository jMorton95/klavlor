using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Persistence.EntityFramework;

public interface IDatabaseConnector
{
    Task<bool> CanConnect();
}

internal sealed class EntityFrameworkDatabaseConnector(DataContext dataContext, ILogger<EntityFrameworkDatabaseConnector> logger) : IDatabaseConnector
{
    public async Task<bool> CanConnect()
    {
        try
        {
            return await dataContext.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database connection check failed.");
            return false;
        }
    }
}
