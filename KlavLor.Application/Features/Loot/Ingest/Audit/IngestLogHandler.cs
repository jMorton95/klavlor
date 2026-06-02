using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Shared;

namespace KlavLor.Application.Features.Loot.Ingest.Audit;

public sealed class IngestLogHandler(
    ILootLogRepository lootLogRepository,
    IngestLogValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result<IngestLogResult>> Handle(IngestLogQuery query)
    {
        // Defense in depth — the endpoint + page are policy-gated, but the handler also runs on
        // direct navigation. Admin supersedes the Auditor role.
        if (!currentUser.IsAdmin && !currentUser.IsInRole(RoleName.Auditor))
            return Result<IngestLogResult>.Failure("You do not have permission to view the sync log.");

        var validationResult = await validator.ValidateAsync(query);
        if (!validationResult.IsValid)
            return Result<IngestLogResult>.ValidationFailure(validationResult.ToDictionary());

        var result = await lootLogRepository.GetIngestLog(query);
        return Result<IngestLogResult>.Success(result);
    }
}
