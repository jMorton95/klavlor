using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Builder.LoadBuilder;

public sealed class LoadBuilderHandler(
    ITemplateRepository templateRepository,
    ICurrentUser currentUser)
{
    public async Task<Result<Template>> Handle(int templateId)
    {
        if (currentUser.UserId is null)
            return Result<Template>.Failure("User not authenticated.");

        var template = await templateRepository.GetById(templateId);

        if (template is null)
            return Result<Template>.Failure("Template not found.");

        if (template.CreatedById != currentUser.UserId && !currentUser.IsAdmin)
            return Result<Template>.Failure("You do not have permission to modify this template.");

        return Result<Template>.Success(template);
    }
}
