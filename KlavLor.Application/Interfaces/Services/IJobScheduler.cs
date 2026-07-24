namespace KlavLor.Application.Interfaces.Services;

// Poll-based scheduler for recurring background services. Each service calls TryClaimRun once a
// minute; it returns true (and atomically claims the slot) when an admin has requested an
// immediate run or the interval has elapsed since the last run, and false otherwise.
public interface IJobScheduler
{
    Task<bool> TryClaimRun(string jobName, TimeSpan interval);
}
