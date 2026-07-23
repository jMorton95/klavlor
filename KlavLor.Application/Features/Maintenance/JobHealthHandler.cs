using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Maintenance;

// A background service's most recent run plus enough derived state for the health panel to
// colour it. IsStuck flags a run still marked Running long after it started — the fingerprint
// of a process that died mid-cycle.
public sealed record JobHealthRow(
    string JobName,
    JobRunOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int ItemsProcessed,
    string? Detail,
    bool IsStuck)
{
    public double? DurationSeconds =>
        FinishedAt is { } f ? Math.Max(0, (f - StartedAt).TotalSeconds) : null;
}

public sealed record JobRunEntry(
    JobRunOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int ItemsProcessed,
    string? Detail)
{
    public double? DurationSeconds =>
        FinishedAt is { } f ? Math.Max(0, (f - StartedAt).TotalSeconds) : null;
}

// Reads the JobRuns operational log for the admin background-jobs health panel. Read-only —
// runs are written exclusively by IJobRunRecorder from the background services themselves.
public sealed class JobHealthHandler(IJobRunRepository jobRuns)
{
    // A Running row older than this almost certainly means the process died mid-cycle.
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<JobHealthRow>> GetHealth()
    {
        var latest = await jobRuns.GetLatestPerJob();
        var now = DateTimeOffset.UtcNow;
        return latest
            .Select(r => new JobHealthRow(
                r.JobName, r.Outcome, r.StartedAt, r.FinishedAt, r.ItemsProcessed, r.Detail,
                IsStuck: r.Outcome == JobRunOutcome.Running && now - r.StartedAt > StuckAfter))
            .ToList();
    }

    public async Task<IReadOnlyList<JobRunEntry>> GetHistory(string jobName)
    {
        var runs = await jobRuns.GetRecentForJob(jobName, 20);
        return runs
            .Select(r => new JobRunEntry(r.Outcome, r.StartedAt, r.FinishedAt, r.ItemsProcessed, r.Detail))
            .ToList();
    }
}
