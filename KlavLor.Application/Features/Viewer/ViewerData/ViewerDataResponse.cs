using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Viewer.ViewerData;

public sealed record ViewerDataResponse(
    Template Template,
    Dictionary<int, DateTimeOffset> CompletionDates,
    bool IsOwner,
    bool CanTrackCompletion
);
