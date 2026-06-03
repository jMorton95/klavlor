namespace KlavLor.Application.Features.CollectionLog;

// A collection-log item row in the admin blacklist UI, with its current exclusion state.
public sealed record ClogItemRow(int ItemId, string Name, bool IsExcluded);
