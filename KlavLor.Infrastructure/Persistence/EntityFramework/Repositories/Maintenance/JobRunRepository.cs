using Microsoft.EntityFrameworkCore;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Maintenance;

internal sealed class JobRunRepository(DataContext dataContext) : IJobRunRepository
{
    public async Task<int> Begin(string jobName, DateTimeOffset startedAt)
    {
        var run = new JobRun { JobName = jobName, StartedAt = startedAt, Outcome = JobRunOutcome.Running };
        dataContext.JobRuns.Add(run);
        await dataContext.SaveChangesAsync();
        dataContext.ChangeTracker.Clear();
        return run.Id;
    }

    public async Task Complete(int id, JobRunOutcome outcome, int itemsProcessed, string? detail, DateTimeOffset finishedAt)
    {
        await dataContext.JobRuns
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Outcome, outcome)
                .SetProperty(r => r.ItemsProcessed, itemsProcessed)
                .SetProperty(r => r.Detail, detail)
                .SetProperty(r => r.FinishedAt, (DateTimeOffset?)finishedAt));
    }

    public async Task PruneOlderThan(DateTimeOffset cutoff)
    {
        await dataContext.JobRuns.Where(r => r.StartedAt < cutoff).ExecuteDeleteAsync();
    }

    public async Task<IReadOnlyList<JobRun>> GetLatestPerJob()
    {
        // Id is monotonic with insert, so max(Id) per job is its most recent run.
        var latestIds = dataContext.JobRuns.GroupBy(r => r.JobName).Select(g => g.Max(x => x.Id));
        return await dataContext.JobRuns.AsNoTracking()
            .Where(r => latestIds.Contains(r.Id))
            .OrderBy(r => r.JobName)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<JobRun>> GetRecentForJob(string jobName, int limit)
    {
        return await dataContext.JobRuns.AsNoTracking()
            .Where(r => r.JobName == jobName)
            .OrderByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync();
    }
}
