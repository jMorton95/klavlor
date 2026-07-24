using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

// Singleton scheduler used by the recurring background services. Owns its own short DI scope per
// poll (like JobRunRecorder), so it never shares the work's DbContext. Fail-safe: any error in
// the claim check returns false (skip this poll) rather than risking an erroneous or double run.
public sealed class JobScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<JobScheduler> logger) : IJobScheduler
{
    public async Task<bool> TryClaimRun(string jobName, TimeSpan interval)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IJobScheduleRepository>();
            return await repository.TryClaim(jobName, interval);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Job scheduler claim check failed for {Job}; skipping this poll", jobName);
            return false;
        }
    }
}
