namespace KlavLor.Domain.Interfaces.Repositories;

public interface IJobScheduleRepository
{
    // Atomically claim a run: returns true (and stamps LastRunAt, clears the manual flag) when a
    // manual request is pending or the interval has elapsed since LastRunAt; false otherwise.
    Task<bool> TryClaim(string jobName, TimeSpan interval);

    // Admin: flag the job to run on its next poll (within ~a minute).
    Task RequestManual(string jobName);

    // Jobs with a manual run still pending — for the admin panel's "requested" indicator.
    Task<IReadOnlyCollection<string>> GetPendingJobNames();
}
