using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Services;

// The result a background cycle reports back so the recorder can log its outcome + row count.
public sealed record JobRunResult(JobRunOutcome Outcome, int ItemsProcessed = 0, string? Detail = null)
{
    public static JobRunResult Ok(int itemsProcessed = 0, string? detail = null) =>
        new(JobRunOutcome.Succeeded, itemsProcessed, detail);

    public static readonly JobRunResult NoWork = new(JobRunOutcome.NoWork);

    public static JobRunResult Failed(string detail) => new(JobRunOutcome.Failed, 0, detail);
}

// Brackets one background-service cycle: writes a Running row, runs the work, then stamps the
// outcome/count/detail (or Failed/Cancelled if it throws). Implemented as a singleton that owns
// its own short-lived scope per write, so it never touches the work's DbContext or requires a
// current user. Services call Track from ExecuteAsync instead of invoking their cycle directly.
public interface IJobRunRecorder
{
    Task Track(string jobName, Func<Task<JobRunResult>> work);
}
