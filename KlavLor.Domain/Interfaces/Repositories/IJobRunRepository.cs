using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface IJobRunRepository
{
    // Insert a Running row and return its id.
    Task<int> Begin(string jobName, DateTimeOffset startedAt);

    // Stamp the finished state onto an existing run (set-based, no tracking).
    Task Complete(int id, JobRunOutcome outcome, int itemsProcessed, string? detail, DateTimeOffset finishedAt);

    // Retention: drop runs older than the cutoff.
    Task PruneOlderThan(DateTimeOffset cutoff);

    // Most-recent run per job — powers the health panel's at-a-glance row per service.
    Task<IReadOnlyList<JobRun>> GetLatestPerJob();

    // Recent runs for one job — the drill-down iteration history.
    Task<IReadOnlyList<JobRun>> GetRecentForJob(string jobName, int limit);
}
