using KlavLor.Application.Common;

namespace KlavLor.Application.Features.Users.Delete;

public sealed record UserDeleteCommand(int? Id) : IdRecord(Id);
