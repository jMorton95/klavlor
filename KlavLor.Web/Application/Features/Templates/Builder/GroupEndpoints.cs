using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Builder.AddGroup;
using KlavLor.Application.Features.Builder.DeleteGroup;
using KlavLor.Application.Features.Builder.UpdateGroupPosition;
using KlavLor.Application.Features.Builder.AssignNodeToGroup;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class GroupEndpoints : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.BuilderGroup.FromApi(), GetGroup).RequireAuthorization(nameof(RoleName.User));
        app.MapPost(AppRoutes.BuilderGroups.FromApi(), AddGroup).RequireAuthorization(nameof(RoleName.User));
        app.MapDelete(AppRoutes.BuilderGroup.FromApi(), DeleteGroup).RequireAuthorization(nameof(RoleName.User));
        app.MapPut(AppRoutes.BuilderGroupPosition.FromApi(), UpdateGroupPosition).RequireAuthorization(nameof(RoleName.User));
        return app.MapPut(AppRoutes.BuilderNodeGroup.FromApi(), AssignNodeToGroup).RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<IResult> GetGroup(
        int id, int groupId,
        ISessionStateManager sessionManager,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var template = await templateRepository.GetById(id);
        if (template is null || template.CreatedById != userId.Value)
            return Microsoft.AspNetCore.Http.Results.NotFound();

        var group = template.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var groupNodes = template.Nodes.Where(n => n.GroupId == groupId).OrderBy(n => n.Id).ToList();
        return IResultExtensions.Component<BuilderGroup>(new
        {
            Group = group,
            TemplateId = id,
            GroupedNodes = groupNodes
        });
    }

    private static async Task<IResult> AddGroup(
        [FromForm] AddGroupCommand command,
        ISessionStateManager sessionManager,
        AddGroupHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(command.TemplateId);
        return IResultExtensions.Component<BuilderCanvas>(new { Template = template });
    }

    private static async Task<IResult> DeleteGroup(
        int id, int groupId,
        ISessionStateManager sessionManager,
        DeleteGroupHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var command = new DeleteGroupCommand { TemplateId = id, GroupId = groupId };
        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        return Microsoft.AspNetCore.Http.Results.NoContent();
    }

    private static async Task<IResult> UpdateGroupPosition(
        int id, int groupId,
        [FromBody] UpdateGroupPositionCommand command,
        ISessionStateManager sessionManager,
        UpdateGroupPositionHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        command.TemplateId = id;
        command.GroupId = groupId;
        var result = await handler.Handle(command, userId.Value);

        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.NoContent()
            : Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);
    }

    private static async Task<IResult> AssignNodeToGroup(
        int id, int nodeId,
        [FromBody] AssignNodeToGroupCommand command,
        ISessionStateManager sessionManager,
        AssignNodeToGroupHandler handler,
        ITemplateRepository templateRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        command.TemplateId = id;
        command.NodeId = nodeId;
        var result = await handler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        var template = await templateRepository.GetById(id);
        return IResultExtensions.Component<BuilderCanvas>(new { Template = template });
    }
}
