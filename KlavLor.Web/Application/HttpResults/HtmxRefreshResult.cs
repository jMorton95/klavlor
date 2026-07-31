namespace KlavLor.Web.Application.HttpResults;

public sealed class HtmxRefreshResult : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.Append("HX-Refresh", "true");
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        await httpContext.Response.WriteAsync(string.Empty);
    }
}
