using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Maintenance;

// Surfaces item/source icons stuck after exhausting automatic retries, and lets an admin
// clear the failure so the backfill service re-attempts them.
public sealed class IconAuditHandler(IItemIconRepository itemIcons, ISourceIconRepository sourceIcons)
{
    public const int Limit = 200;

    public async Task<List<FailedIcon>> GetFailed()
    {
        var items = await itemIcons.GetFailedIcons(Limit);
        var sources = await sourceIcons.GetFailedIcons(Limit);

        return items.Select(i => new FailedIcon(IconKind.Item, i.Id, i.ItemName, i.FailCount, i.LastAttemptAt))
            .Concat(sources.Select(s => new FailedIcon(IconKind.Source, s.Id, s.SourceName, s.FailCount, s.LastAttemptAt)))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task Retry(IconKind kind, int id) =>
        kind == IconKind.Item ? itemIcons.ResetFailure(id) : sourceIcons.ResetFailure(id);
}
