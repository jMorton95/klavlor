namespace KlavLor.Domain.Entities;

public enum JobRunOutcome
{
    Running,   // started, not yet finished (a stuck Running row means the process died mid-cycle)
    Succeeded, // completed and did work
    NoWork,    // completed with nothing to do
    Failed,    // threw
    Cancelled  // interrupted by shutdown
}

// One recorded execution of a background-service cycle, written by IJobRunRecorder and read by
// the admin background-jobs health panel. Deliberately NOT an Entity subclass: it's an
// append-only operational log, not user-edited domain data, so it carries no audit stamp and
// isn't touched by the audit interceptor. Pruned to a rolling window by the recorder.
public sealed class JobRun
{
    public int Id { get; set; }
    public string JobName { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public JobRunOutcome Outcome { get; set; }
    public int ItemsProcessed { get; set; }
    public string? Detail { get; set; }
}
