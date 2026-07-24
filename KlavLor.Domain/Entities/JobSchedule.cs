namespace KlavLor.Domain.Entities;

// Per-job scheduling state, polled ~once a minute by each recurring background service.
// LastRunAt drives the ordinary interval (next run = LastRunAt + interval); ManualRequestedAt,
// when set by an admin, forces the next poll to run regardless of the interval. JobName is the
// business key. Not an Entity subclass — it's operational state, no audit/concurrency token.
public sealed class JobSchedule
{
    public string JobName { get; set; } = "";
    public DateTimeOffset LastRunAt { get; set; }
    public DateTimeOffset? ManualRequestedAt { get; set; }
}
