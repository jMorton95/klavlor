using KlavLor.Application.Common;

namespace KlavLor.Application.Features.Templates.GetById;

public sealed record TemplateGetByIdQuery(int? Id) : IdRecord(Id);
