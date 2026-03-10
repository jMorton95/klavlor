using KlavLor.Application.Features.Viewer.ToggleCompletion;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Viewer;

public sealed class CompletionEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapPost(AppRoutes.ViewerCompletion.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<IResult> Endpoint(
        int id,
        int nodeId,
        ISessionStateManager sessionManager,
        ToggleCompletionHandler toggleHandler,
        ITemplateRepository templateRepository,
        IUserNodeCompletionRepository completionRepository)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return Microsoft.AspNetCore.Http.Results.Unauthorized();

        var command = new ToggleCompletionCommand { TemplateId = id, NodeId = nodeId };
        var result = await toggleHandler.Handle(command, userId.Value);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.BadRequest(result.ErrorMessage);

        // Load just the node to determine how to render it
        var template = await templateRepository.GetById(id);
        if (template is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var node = template.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null) return Microsoft.AspNetCore.Http.Results.NotFound();

        var isCompleted = await completionRepository.IsCompleted(userId.Value, nodeId);

        if (node.GroupId.HasValue)
        {
            // Grouped node — return just the row item
            return IResultExtensions.Component<ViewerGroupItem>(new
            {
                Node = node,
                TemplateId = id,
                IsCompleted = isCompleted,
                CanToggle = true
            });
        }

        // Standalone node — return just the node
        return IResultExtensions.Component<ViewerNode>(new
        {
            Node = node,
            TemplateId = id,
            IsCompleted = isCompleted,
            CanToggle = true
        });
    }
}
