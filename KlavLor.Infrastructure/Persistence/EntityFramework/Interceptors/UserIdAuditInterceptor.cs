using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Interceptors;

public sealed class UserIdAuditInterceptor(ISessionStateManager sessionStateManager) : SaveChangesInterceptor, IAuditInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var userId = sessionStateManager.GetUserSessionId();

        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.SavedById = userId;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
