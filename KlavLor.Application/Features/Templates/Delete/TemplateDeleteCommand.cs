using KlavLor.Application.Common;

namespace KlavLor.Application.Features.Templates.Delete;

public sealed record TemplateDeleteCommand(int? Id) : IdRecord(Id);
