using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

// Records background-service cycles into the JobRuns log. Singleton: it owns its own short DI
// scope per write via IServiceScopeFactory, so recording never shares the work's DbContext and
// a failed/cancelled cycle still gets stamped. All recording is best-effort — a failure to
// write the log must never take down the actual job.
public sealed class JobRunRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<JobRunRecorder> logger) : IJobRunRecorder
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public async Task Track(string jobName, Func<Task<JobRunResult>> work)
    {
        var id = await SafeBegin(jobName);
        try
        {
            var result = await work();
            await SafeComplete(id, result.Outcome, result.ItemsProcessed, result.Detail);
        }
        catch (OperationCanceledException)
        {
            await SafeComplete(id, JobRunOutcome.Cancelled, 0, "cancelled (shutdown)");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background job {Job} threw", jobName);
            await SafeComplete(id, JobRunOutcome.Failed, 0, Truncate(ex.Message));
        }
    }

    private async Task<int> SafeBegin(string jobName)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();
            return await repo.Begin(jobName, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record start of job {Job}", jobName);
            return 0;
        }
    }

    private async Task SafeComplete(int id, JobRunOutcome outcome, int itemsProcessed, string? detail)
    {
        if (id <= 0) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IJobRunRepository>();
            await repo.Complete(id, outcome, itemsProcessed, detail, DateTimeOffset.UtcNow);
            await repo.PruneOlderThan(DateTimeOffset.UtcNow - Retention);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record completion of job run {Id}", id);
        }
    }

    private static string? Truncate(string? s) => s is { Length: > 500 } ? s[..500] : s;
}
