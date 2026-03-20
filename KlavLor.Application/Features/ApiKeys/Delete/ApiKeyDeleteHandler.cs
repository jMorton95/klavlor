using KlavLor.Application.Common;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.ApiKeys.Delete;

public sealed class ApiKeyDeleteHandler(IApiKeyRepository apiKeyRepository)
{
    public async Task<Result> Handle(ApiKeyDeleteCommand command)
    {
        var deleted = await apiKeyRepository.Delete(command.Id);
        return deleted > 0
            ? Result.Success()
            : Result.Failure("API key not found.");
    }
}
