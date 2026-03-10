namespace KlavLor.Web.Application.Results;

public sealed class HtmxRedirectResult(string redirectUrl) : IResult
{
    private string Url { get; } = redirectUrl;
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.Append("HX-Redirect", Url);
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        await httpContext.Response.WriteAsync(string.Empty);
    }
}
