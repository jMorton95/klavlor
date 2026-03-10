namespace KlavLor.Web.Application;

public interface IEndpoint
{
    static abstract RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app);
}
