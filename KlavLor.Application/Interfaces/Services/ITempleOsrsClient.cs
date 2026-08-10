namespace KlavLor.Application.Interfaces.Services;

/// <summary>Why a Temple read produced no usable log. Ok is the only success.</summary>
public enum TempleFetchStatus
{
    Ok,
    /// <summary>Temple knows the player but they have never pressed sync on their collection log.</summary>
    NotSynced,
    /// <summary>Temple has no player by that RSN.</summary>
    NotFound,
    /// <summary>Network failure, timeout, non-200, or a body we couldn't parse.</summary>
    Failed
}

/// <summary>One item a character owns, as Temple reports it.</summary>
public readonly record struct TempleCollectionItem(int ItemId, int Count, DateTimeOffset? ObtainedAt);

/// <summary>A character's whole collection log plus the freshness Temple reports for it.</summary>
public sealed record TempleCollectionLog(
    string Rsn,
    string? DisplayName,
    int GameMode,
    int TotalObtained,
    int TotalAvailable,
    int CategoriesFinished,
    int CategoriesAvailable,
    int? HiscoresRank,
    /// <summary>When the PLAYER last synced to Temple — our staleness signal, not our own fetch time.</summary>
    DateTimeOffset? LastChecked,
    /// <summary>When their log last gained an item. Equal to the stored value means skip the write.</summary>
    DateTimeOffset? LastChanged,
    IReadOnlyList<TempleCollectionItem> Items);

/// <summary>One category and the item ids in it.</summary>
public sealed record TempleCategory(string Slug, string GroupName, IReadOnlyList<int> ItemIds);

public sealed record TempleFetchResult<T>(TempleFetchStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == TempleFetchStatus.Ok && Value is not null;

    public static TempleFetchResult<T> Ok(T value) => new(TempleFetchStatus.Ok, value, null);
    public static TempleFetchResult<T> Fail(TempleFetchStatus status, string? error = null) => new(status, default, error);
}

/// <summary>
/// Read-only client for the TempleOSRS collection-log API.
/// </summary>
/// <remarks>
/// TempleOSRS is the chosen upstream because its item set is byte-identical to ours — 1,712 ids,
/// no difference in either direction — so item id joins straight through with no mapping layer. It
/// is also explicitly open to third-party use, unlike the OSRS Wiki's WikiSync API, which states it
/// is for the wiki only.
///
/// THE TRAP: Temple returns errors as HTTP 200 with an error envelope in the body
/// (<c>{"error":{"Code":402,"Message":"Player has not synced..."}}</c>). Checking the status code
/// alone reports a permanent failure as a success and would wipe a character's stored log. Every
/// response body must be inspected for that envelope before it is trusted.
/// </remarks>
public interface ITempleOsrsClient
{
    /// <summary>One character's full collection log. Never throws; failures come back as a status.</summary>
    Task<TempleFetchResult<TempleCollectionLog>> GetPlayerCollectionLog(string rsn, CancellationToken ct = default);

    /// <summary>The master item list: item id → name. Our reference set, for verification.</summary>
    Task<TempleFetchResult<IReadOnlyDictionary<int, string>>> GetItems(CancellationToken ct = default);

    /// <summary>The 124 categories across Temple's five groups, with their item ids.</summary>
    Task<TempleFetchResult<IReadOnlyList<TempleCategory>>> GetCategories(CancellationToken ct = default);
}
