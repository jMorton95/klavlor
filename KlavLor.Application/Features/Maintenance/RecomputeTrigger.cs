using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Maintenance;

// The one place that says which derived data an admin edit invalidates.
//
// Most admin panels change an INPUT to the luck maths rather than an output: a baseline kill count,
// a delve depth, a rate modifier, a leaderboard exclusion, an intrinsic item value, a source rename.
// The luck leaderboard is precomputed hourly, so before this existed every one of those edits left
// the board quoting the old numbers for up to an hour with nothing on screen to say so — the admin
// had to know to go to the job health panel and hit Run now.
//
// It flags a manual run on the existing poll-and-claim scheduler rather than recomputing inline: a
// leaderboard rebuild walks every character and source, which has no business happening inside an
// admin's request. The refresh services poll once a minute, so "immediately" means within ~60s, and
// the flag is idempotent — ten edits in a row still cost one rebuild.
//
// Deliberately NOT auto-registered by the *Handler convention (it isn't one), so it is registered
// explicitly in ApplicationDependencyConfiguration.
public sealed class RecomputeTrigger(IJobScheduleRepository schedules)
{
    // Call after any write that changes what the luck maths sees: rates, kill counts, depths,
    // exclusions, item values, source names, or an injected drop.
    public Task LuckInputsChanged() =>
        schedules.RequestManual(BackgroundJobNames.LuckLeaderboardRefresh);
}
