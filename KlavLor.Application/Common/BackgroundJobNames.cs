namespace KlavLor.Application.Common;

// Canonical names for the recurring background jobs, shared by the services (which record and
// schedule under these names) and the admin panel (which lists and manually triggers them).
// Keeping them here avoids the service string literals and the admin's trigger list drifting apart.
public static class BackgroundJobNames
{
    public const string DropRateSync = "Drop rate sync";
    public const string CollectionLogSync = "Collection log sync";
    public const string LuckLeaderboardRefresh = "Luck leaderboard refresh";
    public const string LootDerivationBackfill = "Loot derivation backfill";

    // Jobs an admin can trigger on demand (the recurring, poll-scheduled services).
    public static readonly IReadOnlyList<string> Triggerable =
    [
        DropRateSync,
        CollectionLogSync,
        LuckLeaderboardRefresh,
        LootDerivationBackfill
    ];

    public static bool CanTrigger(string jobName) => Triggerable.Contains(jobName);
}
