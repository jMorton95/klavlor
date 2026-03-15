using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Viewer.ViewerData;

public sealed record CompletionInfo(DateTimeOffset CompletedAt, string? Note);

public sealed record ViewerDataResponse(
    Template Template,
    Dictionary<int, CompletionInfo> CompletionDates,
    bool IsOwner,
    bool CanTrackCompletion
);
