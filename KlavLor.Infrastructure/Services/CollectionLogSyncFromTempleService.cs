using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// Pulls each visible character's collection log from TempleOSRS every hour.
/// </summary>
/// <remarks>
/// Built to be a good citizen of someone else's API, which is the main constraint:
///
///   - One request per character per cycle, issued SEQUENTIALLY with a delay between them. Never
///     in parallel — a burst from one host is exactly what gets a third-party API to rate-limit you.
///   - A character whose Temple last-changed timestamp hasn't moved is skipped without a second
///     request, so a settled roster costs one cheap call each and writes nothing.
///   - Characters that keep failing back off exponentially rather than being retried hourly forever.
///   - A failed or empty fetch NEVER clears stored entries. A player who stops syncing to Temple
///     keeps the log we already hold; only the state row records that it has gone stale.
///
/// The taxonomy (categories and their items) is fetched only when we hold none — it is effectively
/// static, changing only when Jagex adds content.
/// </remarks>
public sealed class CollectionLogSyncFromTempleService(
    IServiceScopeFactory scopeFactory,
    IJobRunRecorder jobRuns,
    IJobScheduler scheduler,
    ILogger<CollectionLogSyncFromTempleService> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    /// <summary>Spacing between upstream calls. Deliberate politeness, not a performance figure.</summary>
    private static readonly TimeSpan BetweenCalls = TimeSpan.FromSeconds(2);

    /// <summary>Give up on a character for a while after this many consecutive failures.</summary>
    private const int BackoffAfterFailures = 3;

    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting before reaching out to a third party.
        await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                if (await scheduler.TryClaimRun(BackgroundJobNames.CollectionLogTempleSync, RunInterval))
                    await jobRuns.Track(BackgroundJobNames.CollectionLogTempleSync, () => RunCycle(stoppingToken));
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Collection log Temple sync service failed unexpectedly");
        }
    }

    private async Task<JobRunResult> RunCycle(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new JobRunResult(JobRunOutcome.NoWork, 0, "skipped; previous run still in progress");

        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ICollectionLogRepository>();
            var temple = scope.ServiceProvider.GetRequiredService<ITempleOsrsClient>();

            await EnsureCategories(repository, temple, ct);

            var targets = await repository.GetSyncTargets();
            if (targets.Count == 0)
                return new JobRunResult(JobRunOutcome.NoWork, 0, "no characters with a usable RSN");

            int synced = 0, unchanged = 0, skipped = 0, failed = 0;

            foreach (var target in targets)
            {
                if (ct.IsCancellationRequested) break;

                if (ShouldBackOff(target))
                {
                    skipped++;
                    continue;
                }

                var result = await temple.GetPlayerCollectionLog(target.Rsn, ct);

                if (!result.IsOk)
                {
                    var outcome = result.Status switch
                    {
                        TempleFetchStatus.NotSynced => CollectionLogSyncOutcome.NotSynced,
                        TempleFetchStatus.NotFound => CollectionLogSyncOutcome.NotFound,
                        _ => CollectionLogSyncOutcome.Failed
                    };
                    // Records the outcome only — stored entries are left exactly as they are.
                    await repository.RecordSyncOutcome(target.GameCharacterId, target.Rsn, outcome, result.Error);
                    failed++;
                    logger.LogDebug("Collection log sync for {Rsn}: {Outcome} ({Error})", target.Rsn, outcome, result.Error);
                }
                else if (IsUnchanged(target, result.Value!))
                {
                    // Temple says nothing has moved since we last stored it, so there is nothing to
                    // write. The fetch already happened; this just avoids the database work.
                    await repository.RecordUnchanged(target.GameCharacterId, result.Value!.LastChecked);
                    unchanged++;
                }
                else
                {
                    var applied = await repository.ApplyPlayerLog(target.GameCharacterId, result.Value!);
                    synced++;
                    logger.LogInformation(
                        "Collection log for {Rsn}: +{Added} ~{Updated} -{Removed} ({Total} items)",
                        target.Rsn, applied.Added, applied.Updated, applied.Removed, result.Value!.Items.Count);
                }

                await Task.Delay(BetweenCalls, ct);
            }

            var detail = $"{synced} synced, {unchanged} unchanged, {skipped} backed off, {failed} failed";
            return synced + unchanged > 0
                ? JobRunResult.Ok(synced + unchanged, detail)
                : new JobRunResult(JobRunOutcome.NoWork, 0, detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Collection log Temple sync cycle failed");
            // JobRuns.Detail is bounded; an untrimmed exception message (an EF translation error runs
            // to thousands of characters) fails the insert and loses the failure record entirely.
            var message = ex.Message.ReplaceLineEndings(" ");
            return JobRunResult.Failed(message.Length > 400 ? message[..400] : message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The taxonomy only needs fetching when we hold none. It changes when Jagex adds content, which
    /// an admin can force with a manual job run rather than us polling for it hourly.
    /// </summary>
    private async Task EnsureCategories(ICollectionLogRepository repository, ITempleOsrsClient temple, CancellationToken ct)
    {
        if (await repository.HasCategories()) return;

        var categories = await temple.GetCategories(ct);
        if (!categories.IsOk)
        {
            logger.LogWarning("Could not load collection-log categories from Temple: {Error}", categories.Error);
            return;
        }

        await repository.ReplaceCategories(categories.Value!);
        logger.LogInformation("Loaded {Count} collection-log categories from Temple", categories.Value!.Count);
        await Task.Delay(BetweenCalls, ct);
    }

    /// <summary>
    /// Temple's last_changed is the authoritative "did this player gain anything" signal. Equal to
    /// what we stored means the whole write can be skipped. Null on either side is treated as
    /// changed, because an absent signal is not evidence of no change.
    /// </summary>
    private static bool IsUnchanged(CollectionLogSyncTarget target, TempleCollectionLog log) =>
        // Holding no entries always means re-sync, whatever the timestamps say. Without this, a run
        // that recorded a last-changed but wrote no entries — a partial write, or a parser that
        // silently yielded nothing — would be considered up to date forever and never self-heal.
        target.StoredEntryCount > 0
        && target.StoredLastChanged is { } stored
        && log.LastChanged is { } incoming
        && stored == incoming;

    /// <summary>
    /// After repeated failures, retry on a widening interval instead of every hour. A misspelled RSN
    /// or an account that never syncs would otherwise generate a request an hour forever.
    /// </summary>
    private static bool ShouldBackOff(CollectionLogSyncTarget target)
    {
        if (target.ConsecutiveFailures < BackoffAfterFailures || target.LastSyncedAt is not { } last)
            return false;

        // 3 failures → 4h, 4 → 8h, 5 → 16h, capped at 24h.
        var hours = Math.Min(24, Math.Pow(2, target.ConsecutiveFailures - BackoffAfterFailures + 2));
        return DateTimeOffset.UtcNow - last < TimeSpan.FromHours(hours);
    }
}
