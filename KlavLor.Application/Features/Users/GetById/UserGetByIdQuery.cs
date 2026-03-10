using KlavLor.Application.Common;

namespace KlavLor.Application.Features.Users.GetById;

public sealed record UserGetByIdQuery(int? Id) : IdRecord(Id);
