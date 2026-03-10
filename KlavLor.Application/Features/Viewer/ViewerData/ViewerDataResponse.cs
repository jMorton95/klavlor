using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Viewer.ViewerData;

public sealed record ViewerDataResponse(
    Template Template,
    HashSet<int> CompletedNodeIds,
    bool IsOwner,
    bool CanTrackCompletion
);
